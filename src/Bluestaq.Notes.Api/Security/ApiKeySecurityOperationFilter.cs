using Bluestaq.Notes.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Bluestaq.Notes.Api.Security;

/// <summary>Adds the API-key security requirement to authenticated OpenAPI operations.</summary>
public sealed class ApiKeySecurityOperationFilter : IOperationFilter
{
    /// <summary>Marks authorized operations as requiring the configured API-key header.</summary>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any()
            || !metadata.OfType<IAuthorizeData>().Any())
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(ApiKeyAuthenticationDefaults.Scheme, context.Document)] = [],
        });
    }
}
