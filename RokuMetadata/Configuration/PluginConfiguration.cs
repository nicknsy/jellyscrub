using MediaBrowser.Model.Plugins;

namespace RokuMetadata.Configuration
{
    /// <summary>
    /// Class PluginConfiguration
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool EnableExtractionDuringLibraryScan { get; set; }
        public bool EnableHdThumbnails { get; set; }
        public bool EnableSdThumbnails { get; set; }
        public bool EnableLocalMediaFolderSaving { get; set; }

        public PluginConfiguration()
        {
            EnableHdThumbnails = true;
        }
    }
}
