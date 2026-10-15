using Echo.Core.Protocol;
using Echo.Core.Writes;

namespace Echo.Core.Hosting;

/// <summary>One scan writer/receipt owner, independent of long aggregate reads.</summary>
internal static class ScanWriteExecutor
{
    public static object Execute(CoreRequest request, CoreRequest handshake)
    {
        var generation = handshake.Params?.DatabaseGeneration ?? handshake.DatabaseGeneration;
        if (request.DatabaseGeneration != generation)
            throw new StaleDatabaseGenerationException(generation, request.DatabaseGeneration);
        var path = handshake.Params?.DatabasePath
            ?? throw new InvalidOperationException("Core has not authorized a database.");
        return request.Method switch
        {
            "scanJobCommand" => ScanJobWriter.Execute(path, request.Params?.ScanJobCommand
                ?? throw new ArgumentException("Scan task command required.")),
            "libraryMutation" => LibraryMutations.Execute(path, request.Params?.LibraryMutation
                ?? throw new ArgumentException("Mutation required."), request.Cancellation),
            "scanFinalize" => ScanFinalizeWriter.Execute(path, request.Params?.ScanFinalize
                ?? throw new ArgumentException("Scan finalize command required.")),
            _ => throw new InvalidOperationException("Unknown scan write request."),
        };
    }
}
