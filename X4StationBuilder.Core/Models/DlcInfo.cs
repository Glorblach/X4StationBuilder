namespace X4StationBuilder.Core.Models;

/// <summary>
/// A detected X4 extension (DLC or mod), read from its <c>content.xml</c>.
/// </summary>
public sealed class DlcInfo
{
    /// <summary>Extension id (folder name / <c>content/@id</c>, e.g. <c>ego_dlc_boron</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name from <c>content/@name</c>.</summary>
    public string? Name { get; init; }

    /// <summary>Version string from <c>content/@version</c>.</summary>
    public string? Version { get; init; }

    /// <summary>Author from <c>content/@author</c>.</summary>
    public string? Author { get; init; }

    /// <summary>True if this looks like an official Egosoft DLC (id starts with <c>ego_dlc</c>).</summary>
    public bool IsOfficialDlc => Id.StartsWith("ego_dlc", StringComparison.OrdinalIgnoreCase);
}
