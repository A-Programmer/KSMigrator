#nullable enable

namespace KSDbMigrator;

public class KSDbMigratorOptions
{
    public required string ApplyScriptsFolder { get; set; }
    public required string RollbackScriptsFolder { get; set; }
    public required string BackupsFolder { get; set; }
    public required string ExportsFolder { get; set; }

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
