using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace DeBoetAPI.Common;

public static class OpenApiSetup
{
    private const string BearerScheme = "Bearer";

    /// <summary>
    /// Declares the JWT bearer scheme in the document and marks the operations that need it.
    /// Without this the generated spec looks entirely public, so a client built from it —
    /// Insomnia, Postman, Scalar's own "Authorize" button — has nowhere to put the token.
    /// </summary>
    public static IServiceCollection AddAppOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "The accessToken returned by POST /api/v1/auth/login.",
                };

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;

                // AllowAnonymous wins over any authorize metadata on the surrounding group,
                // which is exactly how the runtime resolves it too.
                var isAnonymous = metadata.OfType<IAllowAnonymous>().Any();
                var needsAuth = !isAnonymous && metadata.OfType<IAuthorizeData>().Any();

                if (needsAuth)
                {
                    operation.Security =
                    [
                        new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference(BearerScheme)] = [],
                        },
                    ];
                }

                return Task.CompletedTask;
            });
        });
}
