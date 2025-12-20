#nullable enable

namespace KSDbMigrator;

public class KSDbMigratorOptions
{
    public string ApplyScriptsFolder { get; set; } = string.Empty;
    public string RollbackScriptsFolder { get; set; } = string.Empty;
    public string BackupsFolder { get; set; } = string.Empty;
    public string ExportsFolder { get; set; } = string.Empty;

    public string? InfrastructureProjectName { get; set; }
    public DatabaseType DatabaseType { get; set; } = DatabaseType.PostgreSQL;
    public string? PgDumpPath { get; set; } = "pg_dump";
    public bool AutoApplyOnStartup { get; set; } = true;
    public bool EnableMigrationEndpoints { get; set; } = false;
    public string MigrationRoute { get; set; } = "api/migrations";
    public string RequiredRole { get; set; } = "Admin";
    public string[] TablesToExport { get; set; } = Array.Empty<string>();
}

public enum DatabaseType
{
    PostgreSQL,
    SQLServer,
    MySQL,
    SQLite
}
