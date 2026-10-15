using System.Buffers;
using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Echo.Bridge.Link;

public static class BridgeMediaResponse
{
    public static (long Start, long Count, bool Partial) Range(string range, long length)
    {
        if (range.Length == 0) return (0, length, false);
        if (length == 0 || !range.StartsWith("bytes=", StringComparison.Ordinal) || range.Contains(',')) throw new BridgeException(416, "invalid_range");
        var parts = range[6..].Split('-');
        if (parts.Length != 2) throw new BridgeException(416, "invalid_range");
        if (parts[0].Length == 0)
        {
            if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix <= 0)
                throw new BridgeException(416, "invalid_range");
            return (Math.Max(0, length - suffix), Math.Min(length, suffix), true);
        }
        if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var start) || start >= length)
            throw new BridgeException(416, "invalid_range");
        var end = length - 1;
        if (parts[1].Length != 0 && !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out end))
            throw new BridgeException(416, "invalid_range");
        end = Math.Min(end, length - 1);
        if (end < start) throw new BridgeException(416, "invalid_range");
        return (start, end - start + 1, true);
    }

    public static async Task Send(HttpContext context, string path, CancellationToken cancellation)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        context.Response.Headers.AcceptRanges = "bytes";
        (long Start, long Count, bool Partial) range;
        try { range = Range(context.Request.Headers.Range.ToString(), file.Length); }
        catch (BridgeException) { context.Response.Headers.ContentRange = $"bytes */{file.Length}"; throw; }
        context.Response.StatusCode = range.Partial ? 206 : 200;
        context.Response.ContentLength = range.Count;
        context.Response.ContentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg", ".flac" => "audio/flac", ".wav" => "audio/wav",
            ".m4a" or ".mp4" or ".alac" => "audio/mp4", ".aac" => "audio/aac", ".ogg" or ".opus" => "audio/ogg",
            ".aif" or ".aiff" => "audio/aiff", _ => "application/octet-stream",
        };
        if (range.Partial) context.Response.Headers.ContentRange = $"bytes {range.Start}-{range.Start + range.Count - 1}/{file.Length}";
        if (HttpMethods.IsHead(context.Request.Method)) return;
        file.Seek(range.Start, SeekOrigin.Begin);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            for (var remaining = range.Count; remaining > 0;)
            {
                var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellation);
                if (read == 0) throw new IOException("Media changed during read.");
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellation);
                remaining -= read;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
