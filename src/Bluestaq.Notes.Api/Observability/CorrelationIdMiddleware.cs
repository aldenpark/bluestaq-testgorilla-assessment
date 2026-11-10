using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Bluestaq.Notes.Api.Observability;

/// <summary>Validates or creates request correlation identifiers and exposes them to logs and traces.</summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>Applies correlation metadata around the downstream request pipeline.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsValid(supplied) ? supplied : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        Activity.Current?.SetTag("request.correlation_id", correlationId);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    private static bool IsValid(string value) =>
        value.Length is >= 1 and <= 100 && CorrelationPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationPattern();
}
