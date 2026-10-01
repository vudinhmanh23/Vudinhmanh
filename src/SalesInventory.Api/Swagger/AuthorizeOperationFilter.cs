using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SalesInventory.Api.Swagger;

// Adds the Bearer security requirement (the padlock in Swagger UI) only to actions that need authorization
public class AuthorizeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        // [AllowAnonymous] on the action overrides [Authorize] on the controller
        if (metadata.OfType<IAllowAnonymous>().Any() || !metadata.OfType<IAuthorizeData>().Any())
        {
            return;
        }

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });

        // The 401/403 responses these endpoints can return
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid token" });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Role not permitted" });
    }
}
