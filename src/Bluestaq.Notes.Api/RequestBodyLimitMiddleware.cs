using Microsoft.Extensions.Options;

namespace Bluestaq.Notes.Api;

/// <summary>Rejects declared request bodies larger than the configured JSON API limit.</summary>
public sealed class RequestBodyLimitMiddleware(RequestDelegate next, IOptions<HardeningOptions> options)
{
    /// <summary>Returns Problem Details before model binding when Content-Length exceeds the limit.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.ContentLength > options.Value.RequestBodyLimitBytes)
        {
            await Results.Problem(
                    detail: $"Request body exceeds {options.Value.RequestBodyLimitBytes} bytes.",
                    statusCode: StatusCodes.Status413PayloadTooLarge)
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
