namespace Echo.Core.Albums;

// One current row only: shared SQLite scalar calls reuse facts and the key.
// Release clears all references; there is no collection-wide managed cache.
internal sealed class AlbumGroupingReadMemo
{
    private string? _title, _path, _trackId, _artist, _key;
    private long? _discNo;
    private AlbumDiscMetadata.Facts? _facts;
    public AlbumDiscMetadata.Facts Read(string title, string path, long? discNo)
    {
        if (_facts is not null && _title == title && _path == path && _discNo == discNo) return _facts;
        _title = title; _path = path; _discNo = discNo;
        _trackId = _artist = _key = null;
        return _facts = AlbumDiscMetadata.Read(title, path, discNo);
    }
    public string Key(string title, string path, string trackId, string artist, long? discNo)
    {
        var facts = Read(title, path, discNo);
        if (_key is not null && _trackId == trackId && _artist == artist) return _key;
        _trackId = trackId; _artist = artist;
        return _key = AlbumIdentity.Key(title, path, trackId, artist, discNo: discNo, facts: facts);
    }
    public void Clear()
    {
        _title = _path = _trackId = _artist = _key = null;
        _facts = null; _discNo = null;
    }
}
