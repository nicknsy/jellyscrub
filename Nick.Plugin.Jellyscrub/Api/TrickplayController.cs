using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace Nick.Plugin.Jellyscrub.Api;

/// <summary>
/// Controller for accessing trickplay data.
/// </summary>
[ApiController]
[Route("Trickplay")]
public class TrickplayController : ControllerBase
{
    private readonly Assembly _assembly;
    private readonly string _trickplayScriptPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrickplayController"/> class.
    /// </summary>
    public TrickplayController()
    {
        _assembly = Assembly.GetExecutingAssembly();
        _trickplayScriptPath = GetType().Namespace + ".trickplay.js";
    }

    /// <summary>
    /// Get embedded javascript file for client-side code.
    /// </summary>
    /// <response code="200">Javascript file successfully returned.</response>
    /// <response code="404">File not found.</response>
    /// <returns>The "trickplay.js" embedded file.</returns>
    [HttpGet("ClientScript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces("application/javascript")]
    public ActionResult GetClientScript()
    {
        var scriptStream = _assembly.GetManifestResourceStream(_trickplayScriptPath);

        if (scriptStream != null)
        {
            return File(scriptStream, "application/javascript");
        }

        return NotFound();
    }
}
