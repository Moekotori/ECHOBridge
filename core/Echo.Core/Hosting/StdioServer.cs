using System.Text.Json;
using Echo.Core.Protocol;
using Echo.Core.Query;

namespace Echo.Core.Hosting;

public sealed class StdioServer : IDisposable
{
    private readonly CoreExecutionLanes _worker;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly TextWriter _log;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _controlWrites = new(ProtocolConstants.MaxPendingControlResponses, ProtocolConstants.MaxPendingControlResponses);
    private readonly CancellationTokenSource _stop = new();
    private readonly byte[] _header = new byte[ProtocolConstants.HeaderSize];
    private readonly Action<CoreRequest>? _validate;
    private readonly Action<HandshakeResult>? _amendHandshake;
    private readonly bool _cancelResponsesOnDisconnect;
    public bool IsStopped => _worker.IsStopped;

    public StdioServer(Stream input, Stream output, TextWriter log, Func<CoreRequest, object?>? execute = null,
        Action<CoreRequest>? validate = null, Action<HandshakeResult>? amendHandshake = null,
        bool cancelResponsesOnDisconnect = false)
    {
        _input = input;
        _output = output;
        _log = log;
        _validate = validate;
        _amendHandshake = amendHandshake;
        _cancelResponsesOnDisconnect = cancelResponsesOnDisconnect;
        // Keep the extra lane experimental until whole-app latency and memory gates pass.
        _worker = new CoreExecutionLanes(execute,
            splitLanes: Environment.GetEnvironmentVariable("ECHO_CORE_QUERY_LANES") != "0");
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var inflight = new List<Task>();
        try
        {
            while (!linked.Token.IsCancellationRequested)
            {
                var headerRead = await ReadExactAsync(_header, linked.Token).ConfigureAwait(false);
                if (headerRead == 0)
                {
                    break;
                }

                if (headerRead < ProtocolConstants.HeaderSize)
                {
                    throw new ProtocolLimitException("stdin closed mid-header.");
                }

                var length = FrameCodec.ReadPayloadLength(_header);
                var payload = new byte[length];
                var payloadRead = await ReadExactAsync(payload, linked.Token).ConfigureAwait(false);
                if (payloadRead < length)
                {
                    throw new ProtocolLimitException("stdin closed mid-frame.");
                }

                var task = HandleFrameAsync(payload, linked.Token);
                lock (inflight)
                {
                    inflight.Add(task);
                }

                _ = task.ContinueWith(static (completed, state) =>
                {
                    var list = (List<Task>)state!;
                    lock (list)
                    {
                        list.Remove(completed);
                    }
                }, inflight, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown, stdin cancel, or fatal protocol close
        }

        finally
        {
            if (_cancelResponsesOnDisconnect) linked.Cancel();
            Task[] remaining;
            lock (inflight) { remaining = [.. inflight]; }

            if (remaining.Length > 0)
            {
                try { await Task.WhenAll(remaining).ConfigureAwait(false); }
                catch { /* Individual frames already wrote structured errors. */ }
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _worker.Dispose();
        _writeGate.Dispose();
        _controlWrites.Dispose();
        _stop.Dispose();
    }

    private async Task HandleFrameAsync(byte[] payload, CancellationToken cancellationToken)
    {
        var payloadLength = payload.Length;
        CoreRequest? request = null;
        var holdsResponseSlot = false;
        try
        {
            request = JsonSerializer.Deserialize(payload, CoreJsonContext.Default.CoreRequest)
                ?? throw new InvalidOperationException("Request JSON was empty.");
            payload = [];
            if (request.Version != ProtocolConstants.ProtocolVersion)
            {
                throw new ProtocolLimitException($"Unsupported protocol version {request.Version}.");
            }
            _validate?.Invoke(request);

            if (request.Method == "ping")
            {
                // Sampling must not wait behind database/media work or open a database.
                await WriteJsonAsync(control: true, writer => CoreResponseJson.WriteOk(writer, request.Id,
                    new PingResult { ResourceUsage = CoreResourceSampler.Sample() }), cancellationToken, request.Id).ConfigureAwait(false);
                return;
            }

            if (request.Method == "cancel")
            {
                var targetId = request.Params?.RequestId ?? 0;
                var report = _worker.Cancel(targetId);
                await WriteJsonAsync(control: true, writer => CoreResponseJson.WriteOk(writer, request.Id, new CancelResult
                {
                    Received = report.Received,
                    QueuedCancelled = report.QueuedCancelled,
                    Running = report.Running,
                    SqlStopped = report.SqlStopped,
                }), cancellationToken, request.Id).ConfigureAwait(false);
                return;
            }

            if (!_worker.TrySubmit(request, payloadLength, out var completion))
            {
                await WriteJsonAsync(control: true, writer => CoreResponseJson.WriteFail(writer, request.Id, new ProtocolLimitException("Core request queue limit exceeded."), _log), cancellationToken, request.Id).ConfigureAwait(false);
                return;
            }

            holdsResponseSlot = true;
            object? result;
            try
            {
                result = await completion.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await WriteJsonAsync(control: false, writer => CoreResponseJson.WriteFail(writer, request.Id, new OperationCanceledException("Core request was cancelled."), _log), cancellationToken, request.Id).ConfigureAwait(false);
                return;
            }

            if (result is HandshakeResult handshake && !handshake.Capabilities.Contains(CoreResourceSampler.Capability))
                handshake.Capabilities.Add(CoreResourceSampler.Capability);
            if (result is HandshakeResult serviceHandshake) _amendHandshake?.Invoke(serviceHandshake);
            await WriteJsonAsync(control: false, writer => CoreResponseJson.WriteOk(writer, request.Id, result), cancellationToken, request.Id).ConfigureAwait(false);
            if (request.Method == "shutdown")
            {
                _stop.Cancel();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            // Do not enqueue another response when the transport is already closing.
        }
        catch (Exception exception)
        {
            var id = request?.Id ?? 0;
            await WriteJsonAsync(control: !holdsResponseSlot, writer => CoreResponseJson.WriteFail(writer, id, exception, _log), cancellationToken, id).ConfigureAwait(false);
            if (exception is ProtocolLimitException)
            {
                _stop.Cancel();
            }
        }
        finally
        {
            if (holdsResponseSlot)
            {
                _worker.ReleaseResponseSlot(request!.Id);
            }
        }
    }

    private async Task WriteJsonAsync(bool control, Action<Utf8JsonWriter> write, CancellationToken cancellationToken, long requestId)
    {
        if (control)
        {
            if (!_controlWrites.Wait(0))
            {
                _stop.Cancel();
                throw new OperationCanceledException("Core control-response budget exhausted.");
            }
        }

        try
        {
            using var buffer = new BoundedEncodeBuffer();
            var encodeStarted = CorePerformanceTrace.Start();
            var frame = buffer.EncodeFrame(write);
            CorePerformanceTrace.Complete(requestId, "encode", encodeStarted, frame.Length);
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var writeStarted = CorePerformanceTrace.Start();
                await _output.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
                CorePerformanceTrace.Complete(requestId, "write", writeStarted, frame.Length);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        finally
        {
            if (control)
            {
                _controlWrites.Release();
            }
        }
    }

    private async Task<int> ReadExactAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await _input.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return offset;
            }

            offset += read;
        }

        return offset;
    }
}
