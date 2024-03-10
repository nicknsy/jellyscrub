using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Nick.Plugin.Jellyscrub.Conversion;

namespace Nick.Plugin.Jellyscrub.Api;

/// <summary>
/// Controller for managing trickplay conversion.
/// </summary>
[ApiController]
[Route("Trickplay/Convert")]
[Authorize(Policy = "RequiresElevation")]
public class ConvertController : ControllerBase
{
    private static ConversionTask _conversionTask;

    public ConvertController(
        ILoggerFactory loggerFactory,
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        ITrickplayManager trickplayManager,
        IApplicationPaths appPaths,
        IServerConfigurationManager configManager
        )
    {
        if (_conversionTask is null)
        {
            _conversionTask = new ConversionTask(
                libraryManager,
                fileSystem,
                trickplayManager,
                appPaths,
                configManager,
                loggerFactory.CreateLogger<ConversionTask>()
                );
        }
    }

    /// <summary>
    /// Start a conversion task.
    /// </summary>
    /// <response code="200">Successfully started task.</response>
    /// <returns>The status code.</returns>
    [HttpPost("ConvertAll")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult ConvertAll()
    {
        // TODO: ForceReconvert
        _ = _conversionTask.ConvertAll().ConfigureAwait(false);
        return Ok();
    }

    /// <summary>
    /// Start a deletion task.
    /// </summary>
    /// <response code="200">Successfully started task.</response>
    /// <returns>The status code.</returns>
    [HttpPost("DeleteAll")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult DeleteAll()
    {
        // TODO: DeleteNonConverted, DeleteNonEmpty
        _ = _conversionTask.DeleteAll().ConfigureAwait(false);
        return Ok();
    }

    /// <summary>
    /// Get the log for a specified task.
    /// </summary>
    /// <param name="type">Type of task to return log for.</param>
    /// <response code="200">Successfully returned log.</response>
    /// <response code="404">Task log not found.</response>
    /// <returns>The JSON log.</returns>
    [HttpGet("Log")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult GetLog([FromQuery, Required] TaskType type)
    {
        switch (type)
        {
            case TaskType.Convert:
                return Content(_conversionTask.GetConvertLog());
            case TaskType.Delete:
                return Content(_conversionTask.GetDeleteLog());
            default:
                return NotFound();
        }
    }

    public enum TaskType
    {
        Convert,
        Delete
    }
}
