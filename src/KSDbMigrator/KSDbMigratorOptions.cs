#nullable enable

namespace KSDbMigrator;

public record KSDbMigratorOptions(
    string ApplyScriptsFolder,
    string RollbackFolder,
    string BackupsFolder,
    string ExportsFolder,
    string? InfrastructureProjectName = null,
    DatabaseType DatabaseType = DatabaseType.PostgreSQL,
    string? PgDumpPath = "pg_dump",
    bool AutoApplyOnStartup = true,
    bool EnableMigrationEndpoints = false,
    string MigrationRoute = "api/migrations",
    string RequiredRole = "Admin",
    string[] TablesToExport = default!)
{
    public static KSDbMigratorOptions Default => new(
        ApplyFolder: "SQLScripts/Apply",
        RollbackFolder: "SQLScripts/Rollback",
        BackupsFolder: "SQLScripts/Backups",
        ExportsFolder: "SQLScripts/Exports"
    );
}

public enum DatabaseType
{
    PostgreSQL,
    SQLServer,
    MySQL,
    SQLite
}
