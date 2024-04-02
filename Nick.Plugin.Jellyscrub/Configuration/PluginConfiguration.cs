using System.Diagnostics;
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
    /// Whether or not to use HW acceleration options set in Jellyfin
    /// for BIF generation. Set to NoEncode on older VAAPI or QSV devices that
    /// can't HW encode MJPEG.
    /// default = None
    /// </summary>
    public HwAccelerationOptions HwAcceleration { get; set; } = HwAccelerationOptions.None;

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
    /// The behavior for the metadata provider used on library scan/update.
    /// Blocking - starts generations, only returns once complete
    /// NonBlocking - starts generation, returns immediately
    /// default = NonBlocking
    /// </summary>
    public MetadataScanBehavior ScanBehavior { get; set; } = MetadataScanBehavior.NonBlocking;

    /// <summary>
    /// The process priority of the ffmpeg .bif generation process.
    /// default = BelowNormal
    /// </summary>
    public ProcessPriorityClass ProcessPriority { get; set; } = ProcessPriorityClass.BelowNormal;

    /// <summary>
    /// Whether to save BIFs in the same media folder as their corresponding video.
    /// default = false
    /// </summary>
    public bool LocalMediaFolderSaving { get; set; } = false;

    /// <summary>
    /// Whether or not the plugin should inject the client-side script tag into jellyfin-web.
    /// default = true
    /// </summary>
    public bool InjectClientScript { get; set; } = true;

    /// <summary>
    /// Whether or not the plugin should style the sliderBubble elements.
    /// default = true
    /// </summary>
    public bool StyleTrickplayContainer { get; set; } = true;

    /// <summary>
    /// Interval, in ms, between each new trickplay image.
    /// default = 10000
    /// </summary>
    public int Interval { get; set; } = 10000;

    /// <summary>
    /// List of target width resolutions, in px, to generates BIFs for.
    /// default = { 320 }
    /// </summary>
    public int[] WidthResolutions { get; set; } = new[] { 320 };

    /// <summary>
    /// Gets or sets the number of parallel processes used to generate the images.
    /// </summary>
    public int ParallelProcesses { get; set; } = 1;

    /// <summary>
    /// Set the number of threads to be used by ffmpeg.
    /// -1 = use default from jellyfin
    /// 0 = default used by ffmpeg
    /// </summary>
    public int ProcessThreads { get; set; } = -1;
}
