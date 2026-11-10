using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Bluestaq.Notes.Api.Security;

/// <summary>Documents common RFC 7807 failure responses consistently across API operations.</summary>
public sealed class ProblemDetailsResponseOperationFilter : IOperationFilter
{
    private static readonly int[] CommonStatuses = [400, 403, 404, 409, 412, 413, 428, 429];

    /// <summary>Adds common Problem Details responses and authentication failures to each operation.</summary>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Responses ??= [];
        var problemSchema = context.SchemaGenerator.GenerateSchema(
            typeof(ProblemDetails),
            context.SchemaRepository);
        foreach (var status in CommonStatuses)
        {
            operation.Responses.TryAdd(status.ToString(), ProblemResponse(status, problemSchema));
        }

        var allowsAnonymous = context.MethodInfo.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any()
            || context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any() == true;
        if (!allowsAnonymous)
        {
            operation.Responses.TryAdd("401", ProblemResponse(401, problemSchema));
        }
    }

    private static OpenApiResponse ProblemResponse(int status, IOpenApiSchema problemSchema) => new()
    {
        Description = ReasonPhrases.GetReasonPhrase(status),
        Content = new Dictionary<string, OpenApiMediaType>
        {
            ["application/problem+json"] = new() { Schema = problemSchema },
        },
    };
}
