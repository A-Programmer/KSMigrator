#nullable enable

namespace KSDbMigrator;

public record KSDbMigratorOptions(
    string ApplyScriptsFolder,
    string RollbackScriptsFolder,
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
        ApplyScriptsFolder: "SQLScripts/Apply",
        RollbackScriptsFolder: "SQLScripts/Rollback",
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
