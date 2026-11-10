using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bluestaq.Notes.Api.Controllers;

/// <summary>Exposes process liveness and dependency readiness for operators.</summary>
[ApiController]
[AllowAnonymous]
[Produces("application/json")]
[Route("health")]
public sealed class HealthController(HealthCheckService healthCheckService) : ControllerBase
{
    /// <summary>Reports process liveness through the backward-compatible health route.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Health() => Ok(LiveResponse());

    /// <summary>Reports whether the API process is running.</summary>
    [HttpGet("live")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Live() => Ok(LiveResponse());

    /// <summary>Reports whether required dependencies are available to serve requests.</summary>
    [HttpGet("ready")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Ready(CancellationToken cancellationToken)
    {
        var report = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains("ready"),
            cancellationToken);
        var response = new HealthResponse(
            report.Status.ToString().ToLowerInvariant(),
            report.Entries.Select(entry => new HealthCheckResponse(
                entry.Key,
                entry.Value.Status.ToString().ToLowerInvariant())).ToArray());
        return report.Status == HealthStatus.Healthy
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    /// <summary>Creates the dependency-free process liveness response.</summary>
    private static HealthResponse LiveResponse() => new("healthy", []);
}

/// <summary>Represents aggregate health and each evaluated dependency.</summary>
public sealed record HealthResponse(string Status, IReadOnlyCollection<HealthCheckResponse> Checks);

/// <summary>Represents one named dependency health result.</summary>
public sealed record HealthCheckResponse(string Name, string Status);
