namespace Echo.Core.Query;

/// <summary>Newest four distinct covers, preserving the reader order for equal timestamps.</summary>
internal sealed class FolderCoverSelection
{
    private const int Limit = 4;
    private readonly List<Candidate> _items = new(Limit);
    private long _sequence;

    public int Count => _items.Count;
    public IEnumerable<string> CoverIds => _items.Select(item => item.CoverId);

    public void Add(string coverId, string updatedAt)
    {
        var candidate = new Candidate(coverId, updatedAt, _sequence++);
        var existing = _items.FindIndex(item => item.CoverId == coverId);
        if (existing >= 0)
        {
            if (StringComparer.Ordinal.Compare(updatedAt, _items[existing].UpdatedAt) <= 0) return;
            _items.RemoveAt(existing);
        }

        var index = 0;
        while (index < _items.Count)
        {
            var comparison = StringComparer.Ordinal.Compare(_items[index].UpdatedAt, updatedAt);
            if (comparison < 0 || (comparison == 0 && _items[index].Sequence > candidate.Sequence)) break;
            index += 1;
        }
        if (index >= Limit) return;
        if (_items.Count == Limit) _items.RemoveAt(Limit - 1);
        _items.Insert(index, candidate);
    }

    private readonly record struct Candidate(string CoverId, string UpdatedAt, long Sequence);
}
