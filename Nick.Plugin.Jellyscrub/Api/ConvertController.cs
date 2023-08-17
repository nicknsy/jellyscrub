using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Nick.Plugin.Jellyscrub.Api;

/// <summary>
/// Controller for managing trickplay conversion.
/// </summary>
[ApiController]
[Route("Trickplay/Convert")]
[Authorize(Policy = "RequiresElevation")]
public class ConvertController : ControllerBase
{
    /// <summary>
    /// Get the log for a specified task.
    /// </summary>
    /// <response code="200">Successfully returned log.</response>
    /// <response code="404">Task log not found.</response>
    /// <returns>The JSON log.</returns>
    [HttpGet("Log")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces(MediaTypeNames.Application.Json)]
    public ActionResult GetLog([FromRoute, Required] TaskType type)
    {
        switch (type)
        {
            case TaskType.Convert:
                return new JsonResult("");
            case TaskType.Delete:
                return new JsonResult("");
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
