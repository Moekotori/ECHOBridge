namespace Echo.Core.Protocol;

public sealed class ScanFinalizeCommand : IRecoverableCommand
{
    public string? OperationId { get; set; }
    public long IssuedAt { get; set; }
    public bool LookupOnly { get; set; }
    public string ScanJobId { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string ScanTimestamp { get; set; } = string.Empty;
    public string FinishedAt { get; set; } = string.Empty;
    public bool MarkMissing { get; set; }
    public int ExpectedAddedCount { get; set; }
}

// Removed keeps this receipt in the bounded library-mutation journal partition.
public sealed record ScanFinalizeResult(string State = "committed", string? InboxBatchId = null, int Removed = 0);
