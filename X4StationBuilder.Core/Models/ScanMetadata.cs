namespace X4StationBuilder.Core.Models;

/// <summary>
/// Metadata describing a single game-folder scan.
/// </summary>
public sealed class ScanMetadata
{
    /// <summary>When the scan completed (UTC).</summary>
    public DateTimeOffset ScannedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Absolute game install path that was scanned.</summary>
    public required string GamePath { get; init; }

    /// <summary>Extensions (DLCs/mods) that were detected and included.</summary>
    public IReadOnlyList<DlcInfo> Dlcs { get; init; } = [];

    /// <summary>Number of modules extracted.</summary>
    public int ModuleCount { get; init; }

    /// <summary>Number of wares extracted.</summary>
    public int WareCount { get; init; }

    /// <summary>Version of the scanner schema that produced the output.</summary>
    public int SchemaVersion { get; init; } = 1;
}
