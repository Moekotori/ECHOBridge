namespace Echo.Core.Protocol;

public interface IRecoverableCommand
{
    string? OperationId { get; set; }
    long IssuedAt { get; set; }
    bool LookupOnly { get; set; }
}

/// <summary>A recoverable command that may also ride a sequenced mutation stream.</summary>
public interface IStreamedCommand : IRecoverableCommand
{
    MutationStream? Stream { get; }
}
