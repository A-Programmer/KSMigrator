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
        var options = new KSDbMigratorOptions();
        configure(options);

        if (string.IsNullOrEmpty(options.ApplyScriptsFolder) ||
            string.IsNullOrEmpty(options.RollbackScriptsFolder) ||
            string.IsNullOrEmpty(options.BackupsFolder) ||
            string.IsNullOrEmpty(options.ExportsFolder))
        {
            throw new InvalidOperationException("All folder paths in KSDbMigratorOptions must be set.");
        }

        services.AddSingleton(options);
        services.AddScoped<IDbMigrator, DbMigrator<TContext>>();

        return services;
    }

    public static IEndpointRouteBuilder MapKSDbMigratorEndpoints<TContext>(
        this IEndpointRouteBuilder endpoints)
        where TContext : DbContext
    {
        var options = endpoints.ServiceProvider.GetRequiredService<KSDbMigratorOptions>();

        // این خط مهم بود که جا افتاده بود!
        if (options.AutoApplyOnStartup)
        {
            using var scope = endpoints.ServiceProvider.CreateScope();
            var migrator = scope.ServiceProvider.GetRequiredService<IDbMigrator>();
            migrator.ApplyPendingScriptsAsync().GetAwaiter().GetResult(); // synchronous برای startup
        }

        if (!options.EnableMigrationEndpoints)
            return endpoints;

        var group = endpoints.MapGroup(options.MigrationRoute);

        if (!string.IsNullOrEmpty(options.RequiredRole))
            group.RequireAuthorization(auth => auth.RequireRole(options.RequiredRole));

        group.MapGet("/status", async (TContext ctx) =>
        {
            var applied = await ctx.Set<AppliedScript>()
                .OrderBy(x => x.AppliedOn)
                .Select(x => x.MigrationName)
                .ToListAsync();

            var applyFolder = options.ApplyScriptsFolder;
            var allScripts = Directory.Exists(applyFolder)
                ? Directory.GetFiles(applyFolder, "*.sql")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(x => x)
                    .ToList()
                : new List<string>();

            var pending = allScripts.Except(applied).ToList();

            return Results.Ok(new
            {
                Applied = applied,
                Pending = pending,
                All = allScripts,
                TotalApplied = applied.Count,
                TotalPending = pending.Count,
                TotalScripts = allScripts.Count
            });
        });

        group.MapPost("/apply", async (IDbMigrator migrator) =>
        {
            await migrator.ApplyPendingScriptsAsync();
            return Results.Ok("All pending migrations have been applied successfully.");
        });

        group.MapPost("/rollback-last", async (IDbMigrator migrator, TContext ctx) =>
        {
            var last = await ctx.Set<AppliedScript>()
                .OrderByDescending(x => x.AppliedOn)
                .FirstOrDefaultAsync();

            if (last == null)
                return Results.BadRequest("No migration has been applied yet.");

            await migrator.RollbackToMigrationAsync(last.MigrationName);
            return Results.Ok($"Rolled back the last migration: {last.MigrationName}");
        });

        group.MapPost("/rollback/{targetVersion}", async (IDbMigrator migrator, string targetVersion) =>
        {
            if (string.IsNullOrWhiteSpace(targetVersion))
                return Results.BadRequest("Target version is required.");

            await migrator.RollbackToMigrationAsync(targetVersion);
            return Results.Ok($"Rolled back to migration: {targetVersion}");
        });

        return endpoints;
    }
}
