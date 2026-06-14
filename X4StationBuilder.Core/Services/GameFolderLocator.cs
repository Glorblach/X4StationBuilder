namespace X4StationBuilder.Core.Services;

/// <summary>
/// Locates and validates an X4: Foundations install folder.
/// </summary>
public static class GameFolderLocator
{
    private static readonly string[] CommonSteamRoots =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\X4 Foundations",
        @"C:\Program Files\Steam\steamapps\common\X4 Foundations",
        @"D:\SteamLibrary\steamapps\common\X4 Foundations",
        @"E:\SteamLibrary\steamapps\common\X4 Foundations",
        @"J:\SteamLibrary\steamapps\common\X4 Foundations",
    ];

    /// <summary>
    /// True if <paramref name="path"/> looks like a valid X4 install: it exists and contains the
    /// base archive <c>01.cat</c> and an <c>extensions</c> folder.
    /// </summary>
    public static bool IsValidGameFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        return File.Exists(Path.Combine(path, "01.cat"))
            && Directory.Exists(Path.Combine(path, "extensions"));
    }

    /// <summary>Returns the first valid install from common Steam locations, or null.</summary>
    public static string? TryFindDefault() =>
        CommonSteamRoots.FirstOrDefault(IsValidGameFolder);
}
