namespace X4StationBuilder.Core.Services.Archive;

/// <summary>
/// One file entry from an X4 <c>.cat</c> index, describing where the file's bytes live
/// inside the paired <c>.dat</c> blob.
/// </summary>
public sealed class CatEntry
{
    /// <summary>Forward-slash relative path inside the archive (e.g. <c>libraries/wares.xml</c>).</summary>
    public required string Path { get; init; }

    /// <summary>Length of the file's bytes in the <c>.dat</c> blob.</summary>
    public required long Size { get; init; }

    /// <summary>Byte offset of the file inside the <c>.dat</c> blob.</summary>
    public required long Offset { get; init; }

    /// <summary>Unix timestamp recorded in the index (informational).</summary>
    public long Timestamp { get; init; }

    /// <summary>MD5 hash hex string recorded in the index (informational; not verified).</summary>
    public string? Hash { get; init; }

    /// <summary>Absolute path of the <c>.dat</c> file that holds this entry's bytes.</summary>
    public required string DatPath { get; init; }
}
