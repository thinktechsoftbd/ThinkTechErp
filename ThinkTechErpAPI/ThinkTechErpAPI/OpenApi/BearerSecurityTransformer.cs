using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ThinkTechErpAPI.OpenApi;

/// <summary>
/// Describes the JWT bearer authentication in the OpenAPI document, so Swagger UI offers an
/// Authorize button and sends the token. Every operation requires it except [AllowAnonymous] ones,
/// matching the fallback authorization policy in Program.cs.
/// </summary>
internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    private const string SchemeName = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "The accessToken from POST /api/security/auth/login, without the \"Bearer \" prefix."
        };
        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            return Task.CompletedTask;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = []
        });
        return Task.CompletedTask;
    }
}
