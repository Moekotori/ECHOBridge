namespace Echo.Core.Protocol;

public sealed class CorePersonalTagTarget
{
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

public sealed class CorePersonalTagDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public bool? Assigned { get; set; }
}

public sealed class CorePersonalTagItemDto
{
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
}

public sealed class CorePersonalTagItemsPage
{
    public List<CorePersonalTagItemDto> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; } = 30;
    public int Total { get; set; }
    public bool HasMore { get; set; }
}
