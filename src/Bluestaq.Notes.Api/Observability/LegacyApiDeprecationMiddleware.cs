using Microsoft.Extensions.Options;

namespace Bluestaq.Notes.Api.Observability;

/// <summary>Adds deprecation and successor metadata to legacy unversioned API responses.</summary>
public sealed class LegacyApiDeprecationMiddleware(RequestDelegate next, IOptions<HardeningOptions> options)
{
    /// <summary>Marks legacy API responses while leaving versioned and health routes unchanged.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api")
            && !context.Request.Path.StartsWithSegments("/api/v1"))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["Deprecation"] = "true";
                context.Response.Headers["Sunset"] = options.Value.LegacyApiSunsetUtc.ToString("R");
                context.Response.Headers.Link = "</api/v1>; rel=\"successor-version\"";
                return Task.CompletedTask;
            });
        }

        await next(context);
    }
}
