# KSDbMigrator

Smart, safe, fully-automatic database migration package for .NET 8+  
Zero manual SQL • Automatic backup • Export/restore on rollback • One-click rollback to any version  
Works with PostgreSQL • SQL Server • MySQL • SQLite

[![NuGet](https://img.shields.io/nuget/v/KSDbMigrator.svg?style=flat-square)](https://www.nuget.org/packages/KSDbMigrator)
[![NuGet downloads](https://img.shields.io/nuget/dt/KSDbMigrator.svg?style=flat-square)](https://www.nuget.org/packages/KSDbMigrator)

## Features

- 100% automatic migration on application startup (production safe)
- Automatic full database backup before every change
- Export data before rollback → restore without duplicates
- Rollback to any previous version with a single API call
- Optional built-in migration management API (`/api/migrations`)
- Works perfectly with Clean Architecture / multi-project solutions
- No manual SQL files required – generated automatically from EF Core migrations

## Installation

```bash
dotnet add package KSDbMigrator
```

## Step-by-step usage (Clean Architecture example)

### 1. Add the tracking entity (only once)

In your `Infrastructure` project, inside your `DbContext.OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // your existing configuration ...

    modelBuilder.AddAppliedScript(); // ← only this line!
}
```

### 2. Register the migrator in `Program.cs` (WebApi project)

```csharp
var builder = WebApplication.CreateBuilder(args);

// your existing services (DbContext, MediatR, etc.)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// KSDbMigrator registration
builder.Services.AddKSDbMigrator<AppDbContext>(opt =>
{
    // Folder structure (relative to Infrastructure project)
    opt.ApplyScriptsFolder    = "SQLScripts/Apply";
    opt.RollbackScriptsFolder = "SQLScripts/Rollback";
    opt.BackupsFolder         = "SQLScripts/Backups";
    opt.ExportsFolder         = "SQLScripts/Exports";

    opt.InfrastructureProjectName = "Project.Infrastructure"; // important!

    opt.DatabaseType = DatabaseType.PostgreSQL; // or SQLServer, MySQL, SQLite
    opt.PgDumpPath   = "pg_dump";               // path on server if not in PATH

    opt.AutoApplyOnStartup = builder.Environment.IsProduction();

    // Optional: enable built-in REST API for migrations
    opt.EnableMigrationEndpoints = true;
    opt.MigrationRoute           = "api/db/migrations";
    opt.RequiredRole             = "Administrator";
});

var app = builder.Build();

// This line activates auto-apply + optional API endpoints
app.MapKSDbMigratorEndpoints<AppDbContext>();

app.Run();
```

### 3. Create the folder structure

Inside `src/Project.Infrastructure` create:

```
SQLScripts/
├── Apply/
├── Rollback/
├── Backups/      ← automatically filled
└── Exports/      ← automatically filled
```

### 4. Generate migrations exactly as you always do

```bash
# Normal EF Core command – nothing changes!
dotnet ef migrations add AddUsersTable \
  --project src/Project.Infrastructure \
  --startup-project src/Project.WebApi
```

### 5. Automatically generate Apply & Rollback scripts (one-liner)

Create a tiny helper script (you only run this once per migration):

**Linux / macOS (`generate-migration.sh`)**

```bash
#!/bin/bash
NAME=$1
INFRA="src/Project.Infrastructure"
WEB="src/Project.WebApi"

dotnet ef migrations add $NAME --project $INFRA --startup-project $WEB

N=$(ls $INFRA/SQLScripts/Apply/*.sql 2>/dev/null | wc -l | xargs)
N=$(printf "%03d" $N)

dotnet ef migrations script --from-migration 0 --to-migration $NAME \
  --output "$INFRA/SQLScripts/Apply/${N}_${NAME}.sql" \
  --idempotent --project $INFRA --startup-project $WEB

dotnet ef migrations script --from-migration $NAME --to-migration 0 \
  --output "$INFRA/SQLScripts/Rollback/${N}_${NAME}_Rollback.sql" \
  --idempotent --project $INFRA --startup-project $WEB

echo "Migration $NAME ready!"
```

**Windows PowerShell (`generate-migration.ps1`)**

```powershell
```powershell
param([string]$Name)
$infra = "src/Project.Infrastructure"
$web = "src/Project.WebApi"

dotnet ef migrations add $Name --project $infra --startup-project $web

$count = (Get-ChildItem "$infra/SQLScripts/Apply" -Filter "*.sql" | Measure-Object).Count
$num = "{0:D3}" -f $count

dotnet ef migrations script --from-migration 0 --to-migration $Name --output "$infra/SQLScripts/Apply/${num}_${Name}.sql" --idempotent --project $infra --startup-project $web
dotnet ef migrations script --from-migration $Name --to-migration 0 --output "$infra/SQLScripts/Rollback/${num}_${Name}_Rollback.sql" --idempotent --project $infra --startup-project $web

Write-Host "Migration $Name ready!"
```

Run it:

```bash
./generate-migration.sh AddUsersTable
```

### 6. That's it!

Run your application:

```bash
dotnet run --project src/Project.WebApi
```

- In Production → all pending migrations are applied automatically + backup taken
- In Development → you still get normal EF Core behavior

### Optional: Built-in migration API (when `EnableMigrationEndpoints = true`)

| Method | Route                        | Description                     |
|-------|------------------------------|---------------------------------|
| GET   | `/api/db/migrations/status`  | Show applied & pending versions |
| POST  | `/api/db/migrations/apply`   | Apply all pending migrations    |
| POST  | `/api/db/migrations/rollback/20240101001_MyMigration` | Rollback to specific version |
| POST  | `/api/db/migrations/rollback-last` | Rollback last migration        |

All endpoints are protected by the role you specified (`Administrator` by default).

## License

MIT © 2025