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
builder.Services.AddKSDbMigrator<ProjectDbContext>(opt =>
{
    opt.ApplyScriptsFolder    = "SQLScripts/Apply";
    opt.RollbackScriptsFolder = "SQLScripts/Rollback";
    opt.BackupsFolder         = "SQLScripts/Backups";
    opt.ExportsFolder         = "SQLScripts/Exports";

    opt.InfrastructureProjectName = "Project.Infrastructure";

    opt.DatabaseType = DatabaseType.PostgreSQL;
    opt.PgDumpPath   = "pg_dump";

    opt.AutoApplyOnStartup = true;

    opt.EnableMigrationEndpoints = true;
    opt.MigrationRoute           = "api/db/migrations";
    // opt.RequiredRole             = "Administrator"; // اگر بخوای
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
set -e

if [ -z "$1" ]; then
  echo "استفاده: ./generate-migration.sh AddBaseEntities"
  exit 1
fi

NAME=$1
INFRA="src/Project.Infrastructure"
WEB="src/Project.WebApi"

echo "ساخت مایگریشن $NAME..."

dotnet ef migrations add $NAME \
  --project $INFRA \
  --startup-project $WEB

# شمارش فایل‌های موجود برای شماره‌گذاری
APPLY_COUNT=$(find "$INFRA/SQLScripts/Apply" -name "*.sql" 2>/dev/null | wc -l || echo 0)
NUM=$(printf "%03d" $((APPLY_COUNT + 1)))

echo "تولید اسکریپت Apply (از ابتدا تا این مایگریشن)..."
dotnet ef migrations script \
  --idempotent \
  --output "$INFRA/SQLScripts/Apply/${NUM}_${NAME}.sql" \
  --project $INFRA \
  --startup-project $WEB

echo "تولید اسکریپت Rollback (معکوس فقط این مایگریشن)..."
dotnet ef migrations script $NAME \
  --idempotent \
  --output "$INFRA/SQLScripts/Rollback/${NUM}_${NAME}_Rollback.sql" \
  --project $INFRA \
  --startup-project $WEB

echo "مایگریشن $NAME با موفقیت ساخته شد!"
echo "حالا برنامه رو اجرا کن → همه چیز خودکار اعمال میشه"
```

```bash
chmod +x generate-migration.sh
./generate-migration.sh AddBaseEntities
```

**Windows PowerShell (`generate-migration.ps1`)**

```powershell
# generate-migration.ps1
# استفاده: .\generate-migration.ps1 AddBaseEntities

param(
    [Parameter(Mandatory=$true)]
    [string]$Name
)

$infra = "src/Project.Infrastructure"
$web = "src/Project.WebApi"

Write-Host "ساخت مایگریشن $Name..." -ForegroundColor Green

dotnet ef migrations add $Name `
    --project $infra `
    --startup-project $web

# شمارش فایل‌های موجود در پوشه Apply برای شماره‌گذاری
$applyPath = Join-Path $infra "SQLScripts/Apply"
$applyFiles = Get-ChildItem -Path $applyPath -Filter "*.sql" -File -ErrorAction SilentlyContinue
$applyCount = if ($applyFiles) { $applyFiles.Count } else { 0 }
$num = "{0:D3}" -f ($applyCount + 1)

Write-Host "تولید اسکریپت Apply (از ابتدا تا این مایگریشن)..." -ForegroundColor Green

dotnet ef migrations script `
    --idempotent `
    --output "$infra/SQLScripts/Apply/${num}_${Name}.sql" `
    --project $infra `
    --startup-project $web

Write-Host "تولید اسکریپت Rollback (معکوس فقط این مایگریشن)..." -ForegroundColor Green

dotnet ef migrations script $Name `
    --idempotent `
    --output "$infra/SQLScripts/Rollback/${num}_${Name}_Rollback.sql" `
    --project $infra `
    --startup-project $web

Write-Host "مایگریشن $Name با موفقیت ساخته شد!" -ForegroundColor Cyan
Write-Host "حالا برنامه رو اجرا کن → همه چیز خودکار اعمال میشه" -ForegroundColor Cyan
```

نحوه استفاده در ویندوز:

فایل رو با نام generate-migration.ps1 در ریشه پروژه‌ات ذخیره کن.
در PowerShell یا Terminal (در VS Code) اجرا کن:
```
PowerShell.\generate-migration.ps1 AddBaseEntities
```
اگر خطای Execution Policy دادی:
```
PowerShellSet-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
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
