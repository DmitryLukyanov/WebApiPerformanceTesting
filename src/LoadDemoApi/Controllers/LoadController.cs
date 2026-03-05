using LoadDemoApi.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoadDemoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = SubscriptionKeyAuthenticationOptions.DefaultScheme)]
public class LoadController : ControllerBase
{
    private readonly ILogger<LoadController> _logger;

    public LoadController(ILogger<LoadController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Emulates load: optional delay and CPU work for performance testing.
    /// </summary>
    /// <param name="delayMs">Delay in milliseconds (default 50, max 5000).</param>
    /// <param name="workIterations">Number of spin iterations to simulate CPU work (default 1000, max 10_000_000).</param>
    [HttpGet]
    [ProducesResponseType(typeof(LoadResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoadResponse>> Get(
        [FromQuery] int delayMs = 50,
        [FromQuery] int workIterations = 1000,
        CancellationToken cancellationToken = default)
    {
        delayMs = Math.Clamp(delayMs, 0, 5000);
        workIterations = Math.Clamp(workIterations, 0, 10_000_000);

        var started = DateTime.UtcNow;

        if (delayMs > 0)
            await Task.Delay(delayMs, cancellationToken);

        // Emulate CPU work
        for (var i = 0; i < workIterations && !cancellationToken.IsCancellationRequested; i++)
            _ = i * 2;

        var elapsed = DateTime.UtcNow - started;

        var response = new LoadResponse(
            Ok: true,
            DelayMs: delayMs,
            WorkIterations: workIterations,
            ElapsedMs: elapsed.TotalMilliseconds,
            Timestamp: started);

        return Ok(response);
    }
}

public record LoadResponse(
    bool Ok,
    int DelayMs,
    int WorkIterations,
    double ElapsedMs,
    DateTime Timestamp);
