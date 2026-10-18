using System.Threading.Channels;
using Echo.Core.Protocol;
using Microsoft.Data.Sqlite;

namespace Echo.Core.Hosting;

public sealed class DatabaseExecutionWorker : IDisposable
{
    public readonly record struct CancelReport(bool Received, bool QueuedCancelled, bool Running, bool SqlStopped);

    private readonly Channel<WorkItem> _channel;
    private readonly SemaphoreSlim _responseSlots;
    private readonly Func<CoreRequest, object?> _execute;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly Dictionary<long, WorkItem> _queued = [];
    private readonly HashSet<long> _responseOwners = [];
    private readonly Action? _onIdle;
    private readonly Action? _onStopped;
    private readonly Action? _interruptRunning;
    private int _queuedCount;
    private long _queuedBytes;
    private long? _runningId;
    private bool _runningPreemptible;
    private bool _runningGrouping;
    private int _runningPriority;
    private CancellationTokenSource? _runningCancellation;
    private bool _disposed;
    private const int PlaybackHydrationLimit = 4;

    public DatabaseExecutionWorker(
        Func<CoreRequest, object?> execute,
        int responseSlots = ProtocolConstants.MaxPendingResponses,
        Action? onIdle = null,
        Action? onStopped = null,
        Action? interruptRunning = null)
    {
        _execute = execute;
        _onIdle = onIdle;
        _onStopped = onStopped;
        _interruptRunning = interruptRunning;
        _responseSlots = new SemaphoreSlim(Math.Max(1, responseSlots), Math.Max(1, responseSlots));
        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(ProtocolConstants.MaxQueuedRequests)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        _thread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = "echo-core-db",
        };
        _thread.Start();
    }

    /// <summary>Queued plus running requests. Lets a dispatcher avoid a lane that is stuck on a long read.</summary>
    public int Load
    {
        get
        {
            lock (_gate)
            {
                return _queuedCount + (_runningId is null ? 0 : 1);
            }
        }
    }

    public bool TrySubmit(CoreRequest request, int payloadBytes, out Task<object?> completion)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var item = new WorkItem(
            request,
            payloadBytes,
            Environment.TickCount64,
            CorePerformanceTrace.Start(),
            new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously),
            CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token));

        var interrupt = false;
        lock (_gate)
        {
            if (_queuedCount >= ProtocolConstants.MaxQueuedRequests
                || _queuedBytes + payloadBytes > ProtocolConstants.MaxQueuedBytes)
            {
                item.Linked.Dispose();
                completion = Task.FromException<object?>(new ProtocolLimitException("Core request queue limit exceeded."));
                return false;
            }

            if (!_channel.Writer.TryWrite(item))
            {
                item.Linked.Dispose();
                completion = Task.FromException<object?>(new ProtocolLimitException("Core request queue limit exceeded."));
                return false;
            }

            _queued[request.Id] = item;
            _queuedCount += 1;
            _queuedBytes += payloadBytes;
            interrupt = _runningPreemptible && (IsCriticalPlaybackRequest(request) || IsForegroundSearch(request))
                && PlaybackReadPriority(request) > _runningPriority;
            // The current owner cannot finish and start another read between
            // selecting the interrupt target and issuing sqlite3_interrupt.
            if (interrupt) _interruptRunning?.Invoke();
        }

        completion = item.Completion.Task;
        return true;
    }

    public CancelReport Cancel(long requestId)
    {
        lock (_gate)
        {
            if (_queued.TryGetValue(requestId, out var queued))
            {
                queued.Linked.Cancel();
                return new CancelReport(true, true, false, false);
            }

            if (_runningId == requestId && (_runningPreemptible || _runningGrouping))
            {
                // Keep the worker owner stable until interrupt has targeted this read.
                _runningCancellation?.Cancel();
                _interruptRunning?.Invoke();
            }
            return new CancelReport(true, false, _runningId == requestId, false);
        }
    }

    public void ReleaseResponseSlot(long requestId)
    {
        lock (_gate)
        {
            if (_responseOwners.Remove(requestId)) _responseSlots.Release();
        }
    }

    public bool IsStopped => !_thread.IsAlive;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _channel.Writer.TryComplete();
        lock (_gate)
        {
            foreach (var item in _queued.Values)
            {
                item.Linked.Cancel();
                item.Completion.TrySetCanceled();
            }
        }

        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            // The worker is blocked in user SQL; the host process owns bounded terminate.
        }

    }

    private void RunLoop()
    {
        try
        {
            var pending = new List<(WorkItem Work, int Order)>();
            var order = 0;
            while (WaitForWork())
            {
                while (_channel.Reader.TryRead(out var item))
                {
                    pending.Add((item, order));
                    order += 1;
                }

                while (pending.Count > 0)
                {
                    while (_channel.Reader.TryRead(out var item))
                    {
                        pending.Add((item, order));
                        order += 1;
                    }

                    var nextIndex = IndexOfHighestPriority(pending);
                    var next = pending[nextIndex].Work;
                    pending.RemoveAt(nextIndex);
                    // A play click is a few primary-key reads. Stop the in-flight
                    // list scan and run that lookup before the scan restarts.
                    if (!ExecuteItem(next))
                    {
                        continue;
                    }

                    ReturnLeftovers(pending);
                    pending.Clear();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // lifetime cancelled
        }
        catch (InvalidOperationException)
        {
            // channel completed
        }
        finally
        {
            lock (_gate)
            {
                foreach (var item in _queued.Values) item.Linked.Dispose();
                _queued.Clear();
                _responseOwners.Clear();
                _responseSlots.Dispose();
            }
            _onStopped?.Invoke();
            _lifetime.Dispose();
        }
    }

    private static int IndexOfHighestPriority(List<(WorkItem Work, int Order)> pending)
    {
        var best = 0;
        for (var index = 1; index < pending.Count; index += 1)
        {
            var priority = PlaybackReadPriority(pending[index].Work.Request);
            var bestPriority = PlaybackReadPriority(pending[best].Work.Request);
            if (priority > bestPriority || (priority == bestPriority && pending[index].Order < pending[best].Order))
            {
                best = index;
            }
        }

        return best;
    }

    private static int PlaybackReadPriority(CoreRequest request)
    {
        if (IsCriticalPlaybackRequest(request))
        {
            return 3;
        }

        if (request.Method is "getTracksByIds" or "getAlbumForTrack")
        {
            return 2;
        }

        if (IsForegroundSearch(request))
        {
            return 2;
        }

        if (request.Method is "getDuplicateIndexSummary" or "getDuplicateHiddenCounts" or "getLikedTrackIds" or "getLikedAlbumIds")
        {
            return 0;
        }

        return 1;
    }

    private static bool IsPlaybackHydration(CoreRequest request) =>
        request.Method == "getTracksByIds"
        && request.Params?.TrackIds is { Count: > 0 and <= PlaybackHydrationLimit };

    private static bool IsCriticalPlaybackRequest(CoreRequest request) =>
        IsPlaybackHydration(request) || request.Method == "playbackCommand";

    private static bool IsForegroundSearch(CoreRequest request) =>
        request.Method == "getTracks" && !string.IsNullOrWhiteSpace(request.Params?.Query?.Search);

    private static bool IsPreemptibleRead(CoreRequest request) => request.Method switch
    {
        "playbackQueueRead" or "getScanCacheStates" or "classifyScanFiles" or "getScanMarkedMissing"
            or "readNativeMetadata" or "getScanCue" or "resolveCoverCandidate" or "prepareAsciiSearchTerms" or "getTracks" or "getAlbumTracks" or "getArtistTracks" or "getGenreTracks"
            or "getArtists" or "getAlbums" or "getGenres" or "getAlbumForTrack"
            or "getDuplicateIndexSummary" or "getDuplicateHiddenCounts"
            or "getLikedTrackIds" or "getLikedAlbumIds" => true,
        "getTracksByIds" => !IsPlaybackHydration(request),
        _ => false,
    };

    private void ReturnLeftovers(List<(WorkItem Work, int Order)> pending)
    {
        lock (_gate)
        {
            foreach (var (work, _) in pending)
            {
                if (_channel.Writer.TryWrite(work))
                {
                    continue;
                }

                _queued.Remove(work.Request.Id);
                _queuedCount = Math.Max(0, _queuedCount - 1);
                _queuedBytes = Math.Max(0, _queuedBytes - work.PayloadBytes);
                work.Completion.TrySetException(new ProtocolLimitException("Core request queue limit exceeded."));
                work.Linked.Dispose();
            }
        }
    }

    private bool WaitForWork()
    {
        var pending = _channel.Reader.WaitToReadAsync(_lifetime.Token).AsTask();
        while (Task.WaitAny([pending], 60_000, _lifetime.Token) == -1)
        {
            // Runs on the owning DB thread, never concurrently with SQL.
            _onIdle?.Invoke();
        }
        return pending.GetAwaiter().GetResult();
    }

    private bool ExecuteItem(WorkItem item)
    {
        lock (_gate)
        {
            _queued.Remove(item.Request.Id);
            _queuedCount = Math.Max(0, _queuedCount - 1);
            _queuedBytes = Math.Max(0, _queuedBytes - item.PayloadBytes);
            if (item.Linked.IsCancellationRequested || item.Completion.Task.IsCompleted)
            {
                item.Completion.TrySetCanceled();
                item.Linked.Dispose();
                return false;
            }

            _runningId = item.Request.Id;
            _runningPreemptible = IsPreemptibleRead(item.Request);
            _runningGrouping = item.Request.Method == "libraryMutation"
                && item.Request.Params?.LibraryMutation?.Operation is "albumGrouping" or "groupingRebuild";
            _runningPriority = PlaybackReadPriority(item.Request);
            _runningCancellation = item.Linked;
        }

        var acquiredSlot = false;
        var yielded = false;
        try
        {
            var waitedMs = Environment.TickCount64 - item.EnqueuedAtMs;
            if (waitedMs >= Math.Max(1, item.Request.DeadlineMs))
            {
                item.Completion.TrySetCanceled();
                return false;
            }

            _responseSlots.Wait(item.Linked.Token);
            acquiredSlot = true;
            lock (_gate) _responseOwners.Add(item.Request.Id);
            item.Linked.Token.ThrowIfCancellationRequested();
            if (Environment.TickCount64 - item.EnqueuedAtMs >= Math.Max(1, item.Request.DeadlineMs))
                throw new OperationCanceledException("Core request expired while waiting for a response slot.");
            CorePerformanceTrace.Complete(item.Request.Id, "queue", item.TraceStarted, item.PayloadBytes);
            var executionStarted = CorePerformanceTrace.Start();
            object? result;
            try
            {
                result = ExecuteUntilYield(item, out yielded);
            }
            finally
            {
                CorePerformanceTrace.Complete(item.Request.Id, "execute", executionStarted);
            }

            if (yielded)
            {
                if (acquiredSlot)
                {
                    ReleaseResponseSlot(item.Request.Id);
                }

                return true;
            }

            item.Linked.Token.ThrowIfCancellationRequested();
            if (!item.Completion.TrySetResult(result) && acquiredSlot)
            {
                ReleaseResponseSlot(item.Request.Id);
            }
        }
        catch (OperationCanceledException exception)
        {
            item.Completion.TrySetCanceled(exception.CancellationToken);
            if (acquiredSlot)
            {
                ReleaseResponseSlot(item.Request.Id);
            }
        }
        catch (Exception exception)
        {
            if (!item.Completion.TrySetException(exception) && acquiredSlot)
            {
                ReleaseResponseSlot(item.Request.Id);
            }
        }
        finally
        {
            lock (_gate)
            {
                if (_runningId == item.Request.Id)
                {
                    _runningId = null;
                    _runningPreemptible = false;
                    _runningGrouping = false;
                    _runningPriority = 0;
                    _runningCancellation = null;
                }
            }

            if (!yielded)
            {
                item.Linked.Dispose();
            }
        }

        return false;
    }

    private object? ExecuteUntilYield(WorkItem item, out bool yielded)
    {
        yielded = false;
        while (true)
        {
            try
            {
                item.Linked.Token.ThrowIfCancellationRequested();
                item.Request.Cancellation = item.Linked.Token;
                return _execute(item.Request);
            }
            catch (SqliteException exception) when ((int)exception.SqliteErrorCode == 9)
            {
                // A user cancellation must never restart the superseded SQL.
                item.Linked.Token.ThrowIfCancellationRequested();
                if (!IsPlaybackHydration(item.Request)
                    && IsPreemptibleRead(item.Request)
                    && HigherPriorityIsWaiting(item.Request)
                    && TryRequeueYielded(item))
                {
                    yielded = true;
                    return null;
                }

                if ((IsPreemptibleRead(item.Request) || IsPlaybackHydration(item.Request)) && item.InterruptRetries < 1)
                {
                    item.InterruptRetries += 1;
                    continue;
                }

                throw;
            }
        }
    }

    private bool HigherPriorityIsWaiting(CoreRequest current)
    {
        lock (_gate)
        {
            foreach (var queued in _queued.Values)
            {
                if (!queued.Linked.IsCancellationRequested && PlaybackReadPriority(queued.Request) > PlaybackReadPriority(current))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private bool TryRequeueYielded(WorkItem item)
    {
        lock (_gate)
        {
            if (item.Linked.IsCancellationRequested || item.Completion.Task.IsCompleted)
            {
                return false;
            }

            if (_queuedCount >= ProtocolConstants.MaxQueuedRequests
                || _queuedBytes + item.PayloadBytes > ProtocolConstants.MaxQueuedBytes
                || !_channel.Writer.TryWrite(item))
            {
                return false;
            }

            _queued[item.Request.Id] = item;
            _queuedCount += 1;
            _queuedBytes += item.PayloadBytes;
            return true;
        }
    }

    private sealed record WorkItem(
        CoreRequest Request,
        int PayloadBytes,
        long EnqueuedAtMs,
        long TraceStarted,
        TaskCompletionSource<object?> Completion,
        CancellationTokenSource Linked)
    {
        public int InterruptRetries { get; set; }
    }
}
