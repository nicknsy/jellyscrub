using MediaBrowser.Model.Plugins;

namespace Nick.Plugin.Jellyscrub.Configuration;

/// <summary>
/// Class PluginConfiguration
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration() {}

    /// <summary>
    /// Determines whether or not trickplays are generated on demand
    /// if client requests are none are available.
    /// default = false
    /// </summary>
    public bool OnDemandGeneration { get; set; } = true;

    /// <summary>
    /// Whether or not to generate BIF as part of library scan.
    /// default = true
    /// </summary>
    public bool ExtractionDuringLibraryScan { get; set; } = true;

    /// <summary>
    /// Whether to save BIFs in the same media folder as their corresponding video.
    /// default = false
    /// </summary>
    public bool LocalMediaFolderSaving { get; set; } = false;

    /// <summary>
    /// Interval, in ms, between each new trickplay image.
    /// default = 10000
    /// </summary>
    public int Interval { get; set; } = 10000;

    /// <summary>
    /// List of target width resolutions, in px, to generates BIFs for.
    /// default = { 320 }
    /// </summary>
    public HashSet<int> WidthResolutions { get; set; } = new HashSet<int> { 320 };
}
