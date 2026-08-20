using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace DeBoetAPI.Common;

public static class OpenApiSetup
{
    private const string BearerScheme = "Bearer";

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
