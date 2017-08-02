using MediaBrowser.Model.Plugins;

namespace RokuMetadata.Configuration
{
    /// <summary>
    /// Class PluginConfiguration
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool EnableExtractionDuringLibraryScan { get; set; }
        public bool EnableLocalMediaFolderSaving { get; set; }
    }
}
