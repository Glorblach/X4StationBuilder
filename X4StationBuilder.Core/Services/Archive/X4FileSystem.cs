namespace X4StationBuilder.Core.Services.Archive;

/// <summary>
/// Describes one mounted X4 archive (base game or an extension) for diagnostics.
/// </summary>
public sealed record MountedArchive(string CatPath, bool IsExtension, string? ExtensionId, int EntryCount);

/// <summary>
/// A virtual, DLC-overlaid view over all of an X4 install's <c>.cat</c>/<c>.dat</c> archives.
/// </summary>
/// <remarks>
/// Base archives (<c>NN.cat</c> at the install root) are mounted first, then every extension's
/// <c>ext_NN.cat</c>. Entries are tracked per path in mount order, so:
/// <list type="bullet">
/// <item><see cref="Get"/> returns the last (highest-precedence) entry for a path.</item>
/// <item><see cref="GetAll"/> returns every entry for a path in mount order, which is what
/// extension XML "diff" patches need (base first, then each overlay).</item>
/// </list>
/// Signature archives (<c>*_sig.cat</c>) are ignored.
/// </remarks>
public sealed class X4FileSystem
{
    private readonly Dictionary<string, List<CatEntry>> _byPath;
    private readonly IReadOnlyList<MountedArchive> _archives;

    private X4FileSystem(Dictionary<string, List<CatEntry>> byPath, IReadOnlyList<MountedArchive> archives)
    {
        _byPath = byPath;
        _archives = archives;
    }

    /// <summary>Archives that were mounted, in load order.</summary>
    public IReadOnlyList<MountedArchive> Archives => _archives;

    /// <summary>All distinct file paths present across the mounted archives.</summary>
    public IEnumerable<string> Paths => _byPath.Keys;

    /// <summary>
    /// Mounts every base and extension archive found under <paramref name="gameRoot"/>.
    /// </summary>
    public static X4FileSystem Mount(string gameRoot)
    {
        var byPath = new Dictionary<string, List<CatEntry>>(StringComparer.OrdinalIgnoreCase);
        var archives = new List<MountedArchive>();

        foreach (var catPath in EnumerateBaseCats(gameRoot))
        {
            archives.Add(MountCat(catPath, byPath, isExtension: false, extensionId: null));
        }

        var extensionsDir = Path.Combine(gameRoot, "extensions");
        if (Directory.Exists(extensionsDir))
        {
            foreach (var extDir in Directory.EnumerateDirectories(extensionsDir)
                         .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var extId = Path.GetFileName(extDir);
                foreach (var catPath in EnumerateExtensionCats(extDir))
                {
                    archives.Add(MountCat(catPath, byPath, isExtension: true, extensionId: extId));
                }
            }
        }

        return new X4FileSystem(byPath, archives);
    }

    /// <summary>Highest-precedence entry for a path, or null if absent.</summary>
    public CatEntry? Get(string path) =>
        _byPath.TryGetValue(Normalize(path), out var list) ? list[^1] : null;

    /// <summary>All entries for a path in mount order (base first), or empty if absent.</summary>
    public IReadOnlyList<CatEntry> GetAll(string path) =>
        _byPath.TryGetValue(Normalize(path), out var list) ? list : Array.Empty<CatEntry>();

    /// <summary>Entries whose path matches <paramref name="predicate"/>, highest-precedence only.</summary>
    public IEnumerable<CatEntry> Find(Func<string, bool> predicate)
    {
        foreach (var (path, list) in _byPath)
        {
            if (predicate(path))
            {
                yield return list[^1];
            }
        }
    }

    private static MountedArchive MountCat(
        string catPath,
        Dictionary<string, List<CatEntry>> byPath,
        bool isExtension,
        string? extensionId)
    {
        var reader = CatDatReader.FromCatFile(catPath);
        foreach (var entry in reader.Entries)
        {
            var key = Normalize(entry.Path);
            if (!byPath.TryGetValue(key, out var list))
            {
                list = new List<CatEntry>(1);
                byPath[key] = list;
            }

            list.Add(entry);
        }

        return new MountedArchive(catPath, isExtension, extensionId, reader.Entries.Count);
    }

    private static IEnumerable<string> EnumerateBaseCats(string gameRoot) =>
        Directory.Exists(gameRoot)
            ? Directory.EnumerateFiles(gameRoot, "*.cat")
                .Where(IsContentCat)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            : [];

    private static IEnumerable<string> EnumerateExtensionCats(string extDir) =>
        Directory.EnumerateFiles(extDir, "*.cat")
            .Where(IsContentCat)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

    private static bool IsContentCat(string path) =>
        !Path.GetFileNameWithoutExtension(path).EndsWith("_sig", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
