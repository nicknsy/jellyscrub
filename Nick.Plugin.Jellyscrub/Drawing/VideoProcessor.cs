using MediaBrowser.Controller.Entities;
using System.Globalization;
using System.Text.Json;

namespace Nick.Plugin.Jellyscrub.Drawing;

public class VideoProcessor
{
    /*
     * Methods for getting storage paths of Manifest files
     */
    public static bool HasManifest(BaseItem item)
    {
        return !string.IsNullOrWhiteSpace(GetExistingManifestPath(item));
    }

    public static string GetNewManifestPath(BaseItem item)
    {
        return JellyscrubPlugin.Instance!.Configuration.LocalMediaFolderSaving ? GetLocalManifestPath(item) : GetInternalManifestPath(item);
    }

    public static string? GetExistingManifestPath(BaseItem item)
    {
        var path = JellyscrubPlugin.Instance!.Configuration.LocalMediaFolderSaving ? GetLocalManifestPath(item) : GetInternalManifestPath(item);

        return File.Exists(path) ? path : null;
    }

    public async static Task<Manifest?> GetManifest(BaseItem item)
    {
        var path = GetExistingManifestPath(item);

        if (path is null) return null;

        using FileStream openStream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Manifest>(openStream);
    }

    private static string GetLocalManifestPath(BaseItem item)
    {
        var folder = Path.Combine(item.ContainingFolderPath, "trickplay");
        var filename = Path.GetFileNameWithoutExtension(item.Path);
        filename += "-" + "manifest.json";

        return Path.Combine(folder, filename);
    }

    private static string GetInternalManifestPath(BaseItem item)
    {
        return Path.Combine(item.GetInternalMetadataPath(), "trickplay", "manifest.json");
    }

    /*
     * Methods for getting storage paths of BIFs
     */
    public static bool HasBif(BaseItem item, int width)
    {
        return !string.IsNullOrWhiteSpace(GetExistingBifPath(item, width));
    }

    public static string? GetExistingBifPath(BaseItem item, int width)
    {
        var path = JellyscrubPlugin.Instance!.Configuration.LocalMediaFolderSaving ? GetLocalBifPath(item, width) : GetInternalBifPath(item, width);

        return File.Exists(path) ? path : null;
    }

    public static string GetNewBifPath(BaseItem item, int width)
    {
        return JellyscrubPlugin.Instance!.Configuration.LocalMediaFolderSaving ? GetLocalBifPath(item, width) : GetInternalBifPath(item, width);
    }

    private static string GetLocalBifPath(BaseItem item, int width)
    {
        var folder = Path.Combine(item.ContainingFolderPath, "trickplay");
        var filename = Path.GetFileNameWithoutExtension(item.Path);
        filename += "-" + width.ToString(CultureInfo.InvariantCulture) + ".bif";

        return Path.Combine(folder, filename);
    }

    private static string GetInternalBifPath(BaseItem item, int width)
    {
        return Path.Combine(item.GetInternalMetadataPath(), "trickplay", width.ToString(CultureInfo.InvariantCulture) + ".bif");
    }
}
