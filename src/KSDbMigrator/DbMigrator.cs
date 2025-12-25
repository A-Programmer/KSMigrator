using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace KSDbMigrator;

public class DbMigrator<TContext> : IDbMigrator where TContext : DbContext
{
    private readonly TContext _context;
    private readonly KSDbMigratorOptions _options;

    public DbMigrator(TContext context, KSDbMigratorOptions options)
    {
        _context = context;
        _options = options;

        var assemblyPath = Assembly.GetAssembly(typeof(TContext))?.Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath) ?? throw new InvalidOperationException("Cannot determine assembly directory.");

        _options.ApplyScriptsFolder = Path.Combine(AppContext.BaseDirectory, "SQLScripts", "Apply");
        _options.RollbackScriptsFolder = Path.Combine(AppContext.BaseDirectory, "SQLScripts", "Rollback");
        _options.BackupsFolder = Path.Combine(AppContext.BaseDirectory, "SQLScripts", "Backups");
        _options.ExportsFolder = Path.Combine(AppContext.BaseDirectory, "SQLScripts", "Exports");
        
        Directory.CreateDirectory(_options.ApplyScriptsFolder);
        Directory.CreateDirectory(_options.RollbackScriptsFolder);
        Directory.CreateDirectory(_options.BackupsFolder);
        Directory.CreateDirectory(_options.ExportsFolder);
    }

    public async Task ApplyPendingScriptsAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct);

        var scripts = Directory.GetFiles(_options.ApplyScriptsFolder, "*.sql")
            .OrderBy(Path.GetFileName)
            .ToList();

        if (!scripts.Any())
            return;

        // بک‌آپ اختیاری — اگر خطا داد، فقط لاگ کن و ادامه بده
        try
        {
            await BackupDatabaseAsync("before_apply", ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Backup skipped (non-critical): {ex.Message}");
        }

        // transaction رو شروع کن
        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            foreach (var scriptPath in scripts)
            {
                var migrationName = Path.GetFileNameWithoutExtension(scriptPath);
                var sql = await File.ReadAllTextAsync(scriptPath, ct);

                await _context.Database.ExecuteSqlRawAsync(sql, ct);

                // اضافه کردن رکورد به applied_scripts — فقط اگر جدول وجود داشته باشه
                try
                {
                    var entity = new AppliedScript
                    {
                        ScriptName = Path.GetFileName(scriptPath),
                        MigrationName = migrationName,
                        AppliedOn = DateTime.UtcNow
                    };

                    await _context.Set<AppliedScript>().AddAsync(entity, ct);
                    await _context.SaveChangesAsync(ct);
                }
                catch (PostgresException ex) when (ex.SqlState == "42P01")
                {
                    // جدول هنوز وجود نداره — اولین اسکریپت داره می‌سازه
                    // نادیده بگیر — در اسکریپت‌های بعدی اضافه می‌شه
                }
            }

            await transaction.CommitAsync(ct);
            Console.WriteLine("All pending migrations applied successfully.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            Console.WriteLine($"Migration failed: {ex.Message}");
            throw;
        }
    }

    public async Task RollbackToMigrationAsync(string targetMigrationName, CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct);

        var applied = await _context.Set<AppliedScript>()
            .OrderByDescending(x => x.AppliedOn)
            .ToListAsync(ct);

        var toRollback = string.IsNullOrEmpty(targetMigrationName) || targetMigrationName == "0"
            ? applied
            : applied.TakeWhile(x => x.MigrationName != targetMigrationName).ToList();

        if (!toRollback.Any())
            throw new InvalidOperationException("No migrations to rollback.");

        await ExportDataAsync(ct);
        await BackupDatabaseAsync($"rollback_to_{targetMigrationName}", ct);

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            foreach (var script in toRollback.AsEnumerable().Reverse())
            {
                var rollbackFile = $"{script.MigrationName}_Rollback.sql";
                var rollbackPath = Path.Combine(_options.RollbackScriptsFolder, rollbackFile);

                if (!File.Exists(rollbackPath))
                    throw new FileNotFoundException($"Rollback script not found: {rollbackFile}");

                var sql = await File.ReadAllTextAsync(rollbackPath, ct);
                await _context.Database.ExecuteSqlRawAsync(sql, ct);

                _context.Set<AppliedScript>().Remove(script);
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            await RestoreDataAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private async Task EnsureConnectionAsync(CancellationToken ct)
    {
        if (!await _context.Database.CanConnectAsync(ct))
            throw new InvalidOperationException("Cannot connect to database.");
    }

    private async Task BackupDatabaseAsync(string operation, CancellationToken ct)
    {
        if (_options.DatabaseType != DatabaseType.PostgreSQL) return;

        // اگر pg_dump وجود نداشته باشه، نادیده بگیر (برای توسعه و تست)
        var psi = new ProcessStartInfo
        {
            FileName = "which",
            Arguments = "pg_dump",
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        try
        {
            using var p = Process.Start(psi);
            await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0) // pg_dump پیدا نشد
            {
                Console.WriteLine("pg_dump not found in container. Skipping backup (safe for development).");
                return;
            }
        }
        catch
        {
            Console.WriteLine("Could not check for pg_dump. Skipping backup.");
            return;
        }

        // اگر pg_dump وجود داشت، بک‌آپ بگیر
        var cs = _context.Database.GetConnectionString()!;
        var builder = new NpgsqlConnectionStringBuilder(cs);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{builder.Database}_{operation}_{timestamp}.backup";
        var path = Path.Combine(_options.BackupsFolder, fileName);

        var args = new[]
        {
            $"--host={builder.Host}",
            $"--port={builder.Port}",
            $"--username={builder.Username}",
            "--format=custom",
            "--blobs",
            "--verbose",
            $"--file={path}",
            builder.Database!
        };

        var backupPsi = new ProcessStartInfo
        {
            FileName = _options.PgDumpPath ?? "pg_dump",
            Arguments = string.Join(" ", args),
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(builder.Password))
            backupPsi.Environment["PGPASSWORD"] = builder.Password;

        using var backupP = Process.Start(backupPsi)!;
        var error = await backupP.StandardError.ReadToEndAsync(ct);
        await backupP.WaitForExitAsync(ct);

        if (backupP.ExitCode != 0)
        {
            Console.WriteLine($"pg_dump failed (non-critical): {error}");
            // ادامه بده — بک‌آپ اختیاریه
        }
        else
        {
            Console.WriteLine($"Backup created: {path}");
        }
    }

    private async Task ExportDataAsync(CancellationToken ct)
    {
        if (!_options.TablesToExport.Any()) return;

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var baseName = $"export_{timestamp}";

        var connectionString = _context.Database.GetConnectionString()!;
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        foreach (var table in _options.TablesToExport)
        {
            var filePath = Path.Combine(_options.ExportsFolder, $"{baseName}_{table}.csv");

            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await using var fileWriter = new StreamWriter(fileStream, Encoding.UTF8);

            using var reader = await conn.BeginTextExportAsync($"COPY {table} TO STDOUT WITH (FORMAT CSV, HEADER)", ct);

            var buffer = new char[8192];
            int charsRead;
            while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileWriter.WriteAsync(buffer, 0, charsRead);
            }
        }
    }

    private async Task RestoreDataAsync(CancellationToken ct)
    {
        if (!_options.TablesToExport.Any()) return;

        var connectionString = _context.Database.GetConnectionString()!;
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        foreach (var file in Directory.GetFiles(_options.ExportsFolder, "export_*.csv"))
        {
            var table = Path.GetFileNameWithoutExtension(file).Split('_').Last();
            var tempTable = $"temp_{Guid.NewGuid():N}".Substring(0, 10);

            await using (var cmd = new NpgsqlCommand($"CREATE TEMP TABLE {tempTable} (LIKE {table} INCLUDING DEFAULTS)", conn))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }

            using var fileStream = File.OpenRead(file);
            using var fileReader = new StreamReader(fileStream, Encoding.UTF8);

            await using (var writer = await conn.BeginTextImportAsync($"COPY {tempTable} FROM STDIN WITH (FORMAT CSV, HEADER)", ct))
            {
                var buffer = new char[8192];
                int charsRead;
                while ((charsRead = await fileReader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await writer.WriteAsync(buffer, 0, charsRead);
                }
            }

            await using (var cmd = new NpgsqlCommand($"INSERT INTO {table} SELECT * FROM {tempTable} ON CONFLICT (id) DO NOTHING", conn))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
    }
}
