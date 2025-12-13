# KSDbMigrator

Smart, safe, cross-platform database migration with automatic backup, export/restore and rollback.

Supports PostgreSQL • SQL Server • MySQL • SQLite

```bash
dotnet add package KSDbMigrator
```

```csharp
builder.Services.AddKSDbMigrator<AppDbContext>(opt =>
{
    opt with
    {
        ApplyScriptsFolder = "SQLScripts/Apply",
        RollbackScriptsFolder = "SQLScripts/Rollback",
        BackupsFolder = "SQLScripts/Backups",
        ExportsFolder = "SQLScripts/Exports",
        EnableMigrationEndpoints = true
    });
});

app.MapKSDbMigratorEndpoints<AppDbContext>();
```

MIT License • https://github.com/yourname/KSDbMigrator
