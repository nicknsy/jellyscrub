using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Nick.Plugin.Jellyscrub.Configuration;

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
    public JellyscrubPlugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<JellyscrubPlugin> logger,
        IServerConfigurationManager configurationManager)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        if (Configuration.InjectClientScript)
        {
            if (!string.IsNullOrWhiteSpace(applicationPaths.WebPath))
            {
                var indexFile = Path.Combine(applicationPaths.WebPath, "index.html");
                if (File.Exists(indexFile))
                {
                    string indexContents = File.ReadAllText(indexFile);
                    string basePath = "";

                    // Get base path from network config
                    try
                    {
                        var networkConfig = configurationManager.GetConfiguration("network");
                        var configType = networkConfig.GetType();
                        var basePathField = configType.GetProperty("BaseUrl");
                        var confBasePath = basePathField?.GetValue(networkConfig)?.ToString()?.Trim('/');

                        if (!string.IsNullOrEmpty(confBasePath)) basePath = "/" + confBasePath.ToString();
                    }
                    catch (Exception e)
                    {
                        logger.LogError("Unable to get base path from config, using '/': {0}", e);
                    }

                    // Don't run if script already exists
                    string scriptReplace = "<script plugin=\"Jellyscrub\".*?></script>";
                    string scriptElement = string.Format("<script plugin=\"Jellyscrub\" version=\"1.0.0.0\" src=\"{0}/Trickplay/ClientScript\"></script>", basePath);

                    if (!indexContents.Contains(scriptElement))
                    {
                        logger.LogInformation("Attempting to inject trickplay script code in {0}", indexFile);

                        // Replace old Jellyscrub scrips
                        indexContents = Regex.Replace(indexContents, scriptReplace, "");

                        // Insert script last in body
                        int bodyClosing = indexContents.LastIndexOf("</body>");
                        if (bodyClosing != -1)
                        {
                            indexContents = indexContents.Insert(bodyClosing, scriptElement);

                            try
                            {
                                File.WriteAllText(indexFile, indexContents);
                                logger.LogInformation("Finished injecting trickplay script code in {0}", indexFile);
                            }
                            catch (Exception e)
                            {
                                logger.LogError("Encountered exception while writing to {0}: {1}", indexFile, e);
                            }
                        }
                        else
                        {
                            logger.LogInformation("Could not find closing body tag in {0}", indexFile);
                        }
                    }
                    else
                    {
                        logger.LogInformation("Found client script injected in {0}", indexFile);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static JellyscrubPlugin? Instance { get; private set; }

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
