using Echo.Core.Protocol;
using Echo.Core.Media;

namespace Echo.Core.Hosting;

/// <summary>DB and lazy media lanes share one bounded admission budget.</summary>
public sealed class CoreExecutionLanes : IDisposable
{
    public const string ParallelMediaCapability = "media.parallelLanes/v1";
    public const string ParallelMediaCapabilityV2 = "media.parallelLanes/v2";
    private const int MediaLaneCount = 8;
    private readonly RequestExecutor _interactiveExecutor;
    private readonly RequestExecutor? _aggregateExecutor;
    private readonly DatabaseExecutionWorker _interactive;
    private readonly DatabaseExecutionWorker? _aggregate;
    private readonly Func<CoreRequest, object?>? _executeOverride;
    private DatabaseExecutionWorker[]? _media;
    private DatabaseExecutionWorker? _scanWrites;
    private int _nextMediaLane;
    private readonly object _gate = new();
    private sealed class Admission(DatabaseExecutionWorker worker, int bytes)
    {
        public DatabaseExecutionWorker Worker { get; } = worker;
        public int Bytes { get; } = bytes;
        public Task<object?>? Completion { get; set; }
    }
    private readonly Dictionary<long, Admission> _admitted = [];
    private long _bytes;
    private long? _barrierId;
    private CoreRequest? _handshake;
    private CoreRequest? _aggregateHandshake;

    public CoreExecutionLanes(Func<CoreRequest, object?>? execute = null, bool splitLanes = true)
    {
        _executeOverride = execute;
        _interactiveExecutor = new(splitLanes ? 3 : 4);
        _interactive = new DatabaseExecutionWorker(request =>
        {
            try { return execute is null ? ExecuteInteractive(request) : execute(request); }
            finally { lock (_gate) { if (_barrierId == request.Id) _barrierId = null; } }
        }, responseSlots: splitLanes ? 1 : ProtocolConstants.MaxPendingResponses,
            onIdle: _interactiveExecutor.ReleaseIdleMaintenance, onStopped: _interactiveExecutor.Dispose,
            interruptRunning: () => _interactiveExecutor.InterruptActiveRead());
        if (splitLanes)
        {
            _aggregateExecutor = new(1);
            _aggregate = new DatabaseExecutionWorker(execute ?? ExecuteAggregate, responseSlots: 1,
                onIdle: () => { _aggregateExecutor.ReleaseIdleMaintenance(); _aggregateExecutor.ReleaseIdleDatabase(); },
                onStopped: _aggregateExecutor.Dispose,
                interruptRunning: () => _aggregateExecutor.InterruptActiveRead());
        }
    }

    public static bool IsAggregate(string method) => method is
        "getPlaybackHistorySummary" or "getPlaybackStatsDashboard" or "getPlaybackStatsDashboardActivity"
        or "getPlaybackStatsInsights" or "getPlaybackMemoryGraph" or "getSteamAchievementHistoryStats"
        or "getFolderOverviews" or "getAlbumFocusFacets" or "rebuildDuplicateIndex"
        or "getDuplicateCleanupCandidatesPage" or "getSummary" or "getLibraryQualityOverview"
        or "getDuplicateIndexSummary" or "getDuplicateHiddenCounts" or "getLikedTrackIds" or "getLikedAlbumIds"
        or "getDuplicateVersionsPage" or "getDuplicatePreviewPage"
        or "libraryMutation" or "scanFinalize" or "scanJobCommand" or "cleanupPage" or "prepareFavorites"
        or "playlistCommand" or "personalTagCommand" or "playbackCommand"
        or "prepareSortKeys" or "commitSortKeys" or "discardSortKeys"
        or "getExistingTrackPaths" or "getScanCacheStates" or "classifyScanFiles" or "getScanMarkedMissing" or "getCueTrackRefs";

    public static bool IsMedia(string method) => method is "readNativeMetadata" or "resolveCoverCandidate" or "prepareAsciiSearchTerms" or "getScanCue";

    public bool TrySubmit(CoreRequest request, int bytes, out Task<object?> completion)
    {
        lock (_gate)
        {
            // Lifecycle operations are barriers, never concurrent with reads on the other connection.
            var barrier = request.Method is "handshake" or "shutdown";
            if (_admitted.ContainsKey(request.Id) || _admitted.Count >= ProtocolConstants.MaxQueuedRequests
                || bytes < 0 || _bytes + bytes > ProtocolConstants.MaxQueuedBytes
                || _barrierId is not null
                || (barrier && _admitted.Values.Any(entry => entry.Completion?.IsCompleted != true)))
            {
                completion = Task.FromException<object?>(new ProtocolLimitException("Core admission limit exceeded."));
                return false;
            }
            var worker = request.Method is "libraryMutation" or "scanFinalize" or "scanJobCommand" ? ScanWriteWorker()
                : IsMedia(request.Method) ? MediaWorker()
                : IsAggregate(request.Method) ? _aggregate ?? _interactive : _interactive;
            var admission = new Admission(worker, bytes);
            _admitted.Add(request.Id, admission);
            if (barrier) _barrierId = request.Id;
            _bytes += bytes;
            if (worker.TrySubmit(request, bytes, out completion))
            {
                admission.Completion = completion;
                return true;
            }
            _admitted.Remove(request.Id);
            if (_barrierId == request.Id) _barrierId = null;
            _bytes -= bytes;
            return false;
        }
    }

    public DatabaseExecutionWorker.CancelReport Cancel(long id)
    {
        lock (_gate)
            return _admitted.TryGetValue(id, out var entry)
                ? entry.Worker.Cancel(id)
                : new(true, false, false, false);
    }

    public void ReleaseResponseSlot(long id)
    {
        lock (_gate)
        {
            if (!_admitted.Remove(id, out var entry)) return;
            if (_barrierId == id) _barrierId = null;
            _bytes -= entry.Bytes;
            entry.Worker.ReleaseResponseSlot(id);
        }
    }

    public bool IsStopped => _interactive.IsStopped && (_aggregate?.IsStopped ?? true)
        && (_scanWrites?.IsStopped ?? true) && (_media?.All(worker => worker.IsStopped) ?? true);

    public void Dispose()
    {
        _scanWrites?.Dispose();
        foreach (var media in _media ?? []) media.Dispose();
        _interactive.Dispose();
        _aggregate?.Dispose();
        lock (_gate) { _admitted.Clear(); _bytes = 0; }
    }

    private object? ExecuteInteractive(CoreRequest request)
    {
        var result = _interactiveExecutor.Execute(request);
        if (request.Method == "handshake") Volatile.Write(ref _handshake, request);
        if (request.Method == "shutdown") Volatile.Write(ref _handshake, null);
        return result;
    }

    private object? ExecuteAggregate(CoreRequest request)
    {
        var handshake = Volatile.Read(ref _handshake)
            ?? throw new InvalidOperationException("Core has not completed handshake.");
        if (!ReferenceEquals(handshake, _aggregateHandshake))
        {
            _aggregateExecutor!.Execute(handshake);
            _aggregateHandshake = handshake;
        }
        return _aggregateExecutor!.Execute(request);
    }

    private object ExecuteMedia(CoreRequest request, NativeMetadataProcess reader, CoverCandidateResolver covers)
    {
        var handshake = Volatile.Read(ref _handshake)
            ?? throw new InvalidOperationException("Core has not completed handshake.");
        if (request.DatabaseGeneration != (handshake.Params?.DatabaseGeneration ?? handshake.DatabaseGeneration))
            throw new StaleDatabaseGenerationException(
                handshake.Params?.DatabaseGeneration ?? handshake.DatabaseGeneration, request.DatabaseGeneration);
        return request.Method switch
        {
            "getScanCue" => request.Params?.ScanCue is { } cue
                ? cue.Files is null ? (object)ScanCueReader.Read(cue, request.Cancellation) : ScanCueReader.ReadBatch(cue, request.Cancellation)
                : throw new ArgumentException("CUE scan request is required."),
            "readNativeMetadata" => reader.Read(request.Params?.NativeMetadata
                ?? throw new ArgumentException("Native metadata request is required."), request.Cancellation),
            "resolveCoverCandidate" => covers.Resolve(request.Params?.CoverCandidate
                ?? throw new ArgumentException("Cover candidate request is required."), request.Cancellation),
            "prepareAsciiSearchTerms" => AsciiSearchTerms.Prepare(request.Params?.AsciiSearchTerms
                ?? throw new ArgumentException("ASCII search request is required.")),
            _ => throw new InvalidOperationException("Unknown Core media request."),
        };
    }

    private DatabaseExecutionWorker ScanWriteWorker() => _scanWrites ??= new DatabaseExecutionWorker(
        _executeOverride ?? (request => ScanWriteExecutor.Execute(request,
            Volatile.Read(ref _handshake) ?? throw new InvalidOperationException("Core has not completed handshake."))),
        responseSlots: 1);

    private DatabaseExecutionWorker MediaWorker()
    {
        // Called under the admission gate; disabled native metadata costs no extra threads or processes.
        // Eight bounded lanes improve scan throughput; idle scanners still release their processes.
        _media ??= Enumerable.Range(0, MediaLaneCount).Select(_ =>
        {
            var reader = new NativeMetadataProcess();
            var covers = new CoverCandidateResolver();
            return new DatabaseExecutionWorker(_executeOverride ?? (request => ExecuteMedia(request, reader, covers)),
                responseSlots: 1, onIdle: () => { reader.Suspend(); covers.Clear(); },
                onStopped: () => { reader.Dispose(); covers.Clear(); });
        }).ToArray();
        // A large-file read can hold one lane for its whole timeout. Round-robin would keep sending every
        // other request behind it while the other lane sits idle, so pick the least-loaded lane and only
        // rotate to break ties.
        var mediaLanes = _media!;
        var start = _nextMediaLane++ % mediaLanes.Length;
        var best = mediaLanes[start];
        var bestLoad = best.Load;
        for (var offset = 1; offset < mediaLanes.Length && bestLoad > 0; offset += 1)
        {
            var candidate = mediaLanes[(start + offset) % mediaLanes.Length];
            var load = candidate.Load;
            if (load < bestLoad)
            {
                best = candidate;
                bestLoad = load;
            }
        }
        return best;
    }
}
