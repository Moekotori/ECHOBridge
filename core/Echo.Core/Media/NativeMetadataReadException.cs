namespace Echo.Core.Media;

public sealed class NativeMetadataReadException(string code) : Exception(MessageFor(code))
{
    public string Code { get; } = code;

    private static string MessageFor(string code) => code switch
    {
        "native_metadata_timeout" => "Native metadata scanner timed out.",
        "native_metadata_unavailable" => "Native metadata scanner is unavailable.",
        "native_metadata_protocol" => "Native metadata scanner protocol mismatch.",
        "native_metadata_crashed" => "Native metadata scanner closed its output.",
        "native_metadata_cleanup_unconfirmed" or "native_metadata_cleanup_pending" => "Native metadata scanner cleanup is not confirmed.",
        _ => "Native metadata scanner failed.",
    };
}
