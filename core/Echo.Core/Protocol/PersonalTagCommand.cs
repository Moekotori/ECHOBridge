namespace Echo.Core.Protocol;

public sealed class PersonalTagCommand : IRecoverableCommand
{
    public string? OperationId { get; set; }
    public long IssuedAt { get; set; }
    public bool LookupOnly { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? NameKey { get; set; }
    public CorePersonalTagTarget? Target { get; set; }
    public bool? Assigned { get; set; }
    public PersonalTagSnapshot? Snapshot { get; set; }
}

public sealed record PersonalTagSnapshot(string Title, string Subtitle);
public sealed record PersonalTagCommandResult(string State = "committed", string? TagId = null);
