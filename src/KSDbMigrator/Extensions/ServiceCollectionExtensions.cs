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
        var options = new KSDbMigratorOptions()
        {
            ApplyScriptsFolder    = "SQLScripts/Apply",
            RollbackScriptsFolder = "SQLScripts/Rollback",
            BackupsFolder         = "SQLScripts/Backups",
            ExportsFolder         = "SQLScripts/Exports",

            InfrastructureProjectName = "Project.Infrastructure",

            DatabaseType = DatabaseType.PostgreSQL,
            PgDumpPath   = "pg_dump",

            AutoApplyOnStartup = true,

            EnableMigrationEndpoints = true,
            MigrationRoute           = "api/db/migrations",
            // RequiredRole             = "Administrator", // اگر بخوای
        };
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

        group.MapGet("/status", async (TContext ctx, KSDbMigratorOptions options) =>
        {
            var applied = await ctx.Set<AppliedScript>()
                .AsNoTracking()
                .OrderBy(x => x.AppliedOn)
                .Select(x => x.MigrationName)
                .ToListAsync();
            var applyFolder = Path.Combine(AppContext.BaseDirectory, "..", options.ApplyScriptsFolder);
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
            var lastApplied = await ctx.Set<AppliedScript>()
                .OrderByDescending(x => x.AppliedOn)
                .FirstOrDefaultAsync();

            if (lastApplied == null)
                return Results.BadRequest("No migration has been applied yet. Nothing to rollback.");

            await migrator.RollbackToMigrationAsync(lastApplied.MigrationName);
            return Results.Ok($"Successfully rolled back the last migration: {lastApplied.MigrationName}");
        });

        group.MapPost("/rollback/{targetVersion}", async (IDbMigrator migrator, string targetVersion) =>
        {
            if (string.IsNullOrWhiteSpace(targetVersion))
                return Results.BadRequest("Target version is required.");

            await migrator.RollbackToMigrationAsync(targetVersion);
            return Results.Ok($"Successfully rolled back to migration: {targetVersion}");
        });

        return endpoints;
    }
}
