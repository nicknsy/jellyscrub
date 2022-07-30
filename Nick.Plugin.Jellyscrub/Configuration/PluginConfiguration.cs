using MediaBrowser.Model.Plugins;

namespace Nick.Plugin.Jellyscrub.Configuration;

/// <summary>
/// Class PluginConfiguration
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    public bool EnableExtractionDuringLibraryScan { get; set; }
    public bool EnableLocalMediaFolderSaving { get; set; }
}
