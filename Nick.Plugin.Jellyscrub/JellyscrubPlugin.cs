using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.IO;
using MediaBrowser.Controller.MediaEncoding;
using Nick.Plugin.Jellyscrub.Configuration;
using Nick.Plugin.Jellyscrub.Drawing;

namespace Nick.Plugin.Jellyscrub;

/// <summary>
/// Jellyscrub plugin.
/// </summary>
public class JellyscrubPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <inheritdoc />
    public override string Name => "Jellyscrub";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("a84a949d-4b73-4099-aacb-8341b4da17ba");

    /// <inheritdoc />
    public override string Description => "Smooth mouse-over video scrubbing previews.";

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyscrubPlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory"/> interface.</param>
    /// <param name="configurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    /// <param name="fileSystem">Instance of the <see cref="IFileSystem"/> interface.</param>
    public JellyscrubPlugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<JellyscrubPlugin> logger,
        IMediaEncoder mediaEncoder,
        ILoggerFactory loggerFactory,
        IServerConfigurationManager configurationManager,
        IFileSystem fileSystem)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        OldMediaEncoder = new OldMediaEncoder(loggerFactory.CreateLogger<OldMediaEncoder>(), mediaEncoder, configurationManager, fileSystem);

        if (Configuration.InjectClientScript)
        {
            if (!string.IsNullOrWhiteSpace(applicationPaths.WebPath))
            {
                var indexFile = Path.Combine(applicationPaths.WebPath, "index.html");
                if (File.Exists(indexFile))
                {
                    string indexContents = File.ReadAllText(indexFile);
                    if (!indexContents.Contains("/Trickplay/ClientScript"))
                    {
                        logger.LogInformation("Attempting to inject trickplay script code in {0}", indexFile);

                        int bodyClosing = indexContents.LastIndexOf("</body>");
                        if (bodyClosing != -1)
                        {
                            indexContents = indexContents.Insert(bodyClosing, "<script plugin=\"Jellyscrub\" version=\"1.0.0.0\" src=\"/Trickplay/ClientScript\"></script>");
                            File.WriteAllText(indexFile, indexContents);

                            logger.LogInformation("Finished injecting trickplay script code in {0}", indexFile);
                        }
                        else
                        {
                            logger.LogInformation("Could not find closing body tag in {0}", indexFile);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static JellyscrubPlugin? Instance { get; private set; }

    /// <summary>
    /// Gets the OldMediaEncoder instance.
    /// </summary>
    /// <value>The instance.</value>
    public OldMediaEncoder? OldMediaEncoder { get; private set; }


    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "Jellyscrub",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        };
    }
}
