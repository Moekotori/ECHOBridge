namespace Echo.Core.Protocol;

public sealed class SortKeyMaintenanceParams
{
    public string? Kind { get; set; }
    public string? Token { get; set; }
    public string[][]? Keys { get; set; }
}

public sealed record SortKeyMaintenanceResult(bool Ready, string? Token = null,
    string[][]? Values = null, int WroteRows = 0);
