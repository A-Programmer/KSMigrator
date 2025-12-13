using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KSDbMigrator;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKSDbMigrator<TContext>(
        this IServiceCollection services,
        Action<KSDbMigratorOptions> configure)
        where TContext : DbContext
    {
        var options = KSDbMigratorOptions.Default;
        configure(options);
        services.AddSingleton(options);
        services.AddScoped<IDbMigrator, DbMigrator<TContext>>();
        return services;
    }

    public static IEndpointRouteBuilder MapKSDbMigratorEndpoints<TContext>(
        this IEndpointRouteBuilder endpoints)
        where TContext : DbContext
    {
        var options = endpoints.ServiceProvider.GetRequiredService<KSDbMigratorOptions>();

        if (!options.EnableMigrationEndpoints) return endpoints;

        var group = endpoints.MapGroup(options.MigrationRoute);

        if (!string.IsNullOrEmpty(options.RequiredRole))
            group.RequireAuthorization(auth => auth.RequireRole(options.RequiredRole));

        group.MapGet("/status", () => Results.Ok(new { Message = "Status endpoint" }));
        group.MapPost("/apply", () => Results.Ok("Applied"));
        group.MapPost("/rollback/{v}", (string v) => Results.Ok($"Rolled back to {v}"));

        return endpoints;
    }
}
