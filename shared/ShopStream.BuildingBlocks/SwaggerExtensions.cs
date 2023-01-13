using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace ShopStream.BuildingBlocks;

public static class SwaggerExtensions
{
    public static IServiceCollection AddShopStreamSwagger(this IServiceCollection services, string title) =>
        services.AddEndpointsApiExplorer().AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = title, Version = "v1" });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the access token returned by /api/auth/login"
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                    Array.Empty<string>()
                }
            });
            options.CustomSchemaIds(t => t.FullName?.Replace('+', '.'));
        });
}
