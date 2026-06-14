using System.Text;

namespace X4StationBuilder.Core.Services.Archive;

/// <summary>
/// Reads an X4 <c>.cat</c> index file and the bytes of its entries from the paired <c>.dat</c> blob.
/// </summary>
/// <remarks>
/// <para>
/// A <c>.cat</c> file is a plain-text index with one line per file:
/// <c>relative/path size timestamp md5hash</c>. The relative path may itself contain spaces,
/// so the three trailing fields (size, timestamp, hash) are parsed from the end of the line.
/// </para>
/// <para>
/// The paired <c>.dat</c> file is the concatenation of every entry's bytes in the order listed
/// in the <c>.cat</c>; an entry's offset is therefore the running sum of all preceding sizes.
/// Signature archives (<c>*_sig.cat</c>) are not handled here and should be skipped by callers.
/// </para>
/// </remarks>
public sealed class CatDatReader
{
    private readonly IReadOnlyList<CatEntry> _entries;

    private CatDatReader(IReadOnlyList<CatEntry> entries) => _entries = entries;

    /// <summary>All entries indexed by this <c>.cat</c>, in archive order.</summary>
    public IReadOnlyList<CatEntry> Entries => _entries;

    /// <summary>
    /// Parses a <c>.cat</c> file. The paired <c>.dat</c> is assumed to sit next to it with the same
    /// base name (<c>foo.cat</c> → <c>foo.dat</c>).
    /// </summary>
    public static CatDatReader FromCatFile(string catPath)
    {
        var datPath = System.IO.Path.ChangeExtension(catPath, ".dat");
        var lines = File.ReadAllLines(catPath);
        return new CatDatReader(ParseEntries(lines, datPath));
    }

    /// <summary>Parses raw <c>.cat</c> lines; used directly by tests with a synthetic <c>.dat</c> path.</summary>
    public static CatDatReader FromCatLines(IEnumerable<string> lines, string datPath) =>
        new(ParseEntries(lines, datPath));

    private static List<CatEntry> ParseEntries(IEnumerable<string> lines, string datPath)
    {
        var entries = new List<CatEntry>();
        long offset = 0;

        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var line = raw.TrimEnd('\r', '\n');

            // The last three space-separated tokens are size, timestamp, hash; the rest is the path.
            var lastSpace = line.LastIndexOf(' ');
            if (lastSpace < 0)
            {
                continue;
            }

            var hash = line[(lastSpace + 1)..];
            var secondSpace = line.LastIndexOf(' ', lastSpace - 1);
            if (secondSpace < 0)
            {
                continue;
            }

            var timestampToken = line[(secondSpace + 1)..lastSpace];
            var thirdSpace = line.LastIndexOf(' ', secondSpace - 1);
            if (thirdSpace < 0)
            {
                continue;
            }

            var sizeToken = line[(thirdSpace + 1)..secondSpace];
            var path = line[..thirdSpace];

            if (!long.TryParse(sizeToken, out var size))
            {
                continue;
            }

            long.TryParse(timestampToken, out var timestamp);

            entries.Add(new CatEntry
            {
                Path = path.Replace('\\', '/'),
                Size = size,
                Offset = offset,
                Timestamp = timestamp,
                Hash = hash,
                DatPath = datPath,
            });

            offset += size;
        }

        return entries;
    }

    /// <summary>Reads the raw bytes for a single entry from its <c>.dat</c> blob.</summary>
    public static byte[] ReadBytes(CatEntry entry)
    {
        using var stream = new FileStream(
            entry.DatPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        stream.Seek(entry.Offset, SeekOrigin.Begin);

        var buffer = new byte[entry.Size];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return buffer;
    }

    /// <summary>Reads an entry's bytes and decodes them as UTF-8 text.</summary>
    public static string ReadText(CatEntry entry) =>
        Encoding.UTF8.GetString(ReadBytes(entry));
}
