using System.Xml.Linq;
using X4StationBuilder.Core.Models;

namespace X4StationBuilder.Core.Services;

/// <summary>
/// Detects X4 extensions (DLCs and mods) by reading each <c>extensions\*\content.xml</c>.
/// </summary>
public static class DlcDetector
{
    /// <summary>Returns all extensions found under <paramref name="gameRoot"/>, ordered by id.</summary>
    public static IReadOnlyList<DlcInfo> Detect(string gameRoot)
    {
        var extensionsDir = Path.Combine(gameRoot, "extensions");
        if (!Directory.Exists(extensionsDir))
        {
            return [];
        }

        var result = new List<DlcInfo>();
        foreach (var dir in Directory.EnumerateDirectories(extensionsDir))
        {
            var contentPath = Path.Combine(dir, "content.xml");
            if (!File.Exists(contentPath))
            {
                continue;
            }

            var info = ParseContent(contentPath, Path.GetFileName(dir));
            if (info is not null)
            {
                result.Add(info);
            }
        }

        return result
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DlcInfo? ParseContent(string contentPath, string folderName)
    {
        try
        {
            var doc = XDocument.Load(contentPath);
            var content = doc.Root;
            if (content is null || content.Name.LocalName != "content")
            {
                return new DlcInfo { Id = folderName };
            }

            return new DlcInfo
            {
                Id = content.Attribute("id")?.Value ?? folderName,
                Name = content.Attribute("name")?.Value,
                Version = content.Attribute("version")?.Value,
                Author = content.Attribute("author")?.Value,
            };
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            return new DlcInfo { Id = folderName };
        }
    }
}
