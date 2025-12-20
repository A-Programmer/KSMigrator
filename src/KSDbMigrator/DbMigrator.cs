using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;
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

        Directory.CreateDirectory(_options.ApplyScriptsFolder);
        Directory.CreateDirectory(_options.RollbackScriptsFolder);
        Directory.CreateDirectory(_options.BackupsFolder);
        Directory.CreateDirectory(_options.ExportsFolder);
    }

    public async Task ApplyPendingScriptsAsync(CancellationToken ct = default)
    {
        await EnsureConnectionAsync(ct);

        HashSet<string> appliedSet = new();

        bool tableExists = false;
        try
        {
            var appliedList = await _context.Set<AppliedScript>()
                .Select(x => x.MigrationName)
                .ToListAsync(ct);

            appliedSet = appliedList.ToHashSet(StringComparer.OrdinalIgnoreCase);
            tableExists = true;
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // جدول وجود نداره
        {
            tableExists = false;
        }

        var scripts = Directory.GetFiles(_options.ApplyScriptsFolder, "*.sql")
            .OrderBy(Path.GetFileName)
            .Select(f => new { Path = f, Name = Path.GetFileNameWithoutExtension(f) })
            .Where(f => !appliedSet.Contains(f.Name))
            .Select(f => f.Path)
            .ToList();

        if (!scripts.Any())
            return;

        await BackupDatabaseAsync("before_apply", ct);

        await using var transaction = await _context.Database.BeginTransactionAsync(ct);

        try
        {
            foreach (var scriptPath in scripts)
            {
                var migrationName = Path.GetFileNameWithoutExtension(scriptPath);
                var sql = await File.ReadAllTextAsync(scriptPath, ct);

                await _context.Database.ExecuteSqlRawAsync(sql, ct);

                // اگر جدول وجود داشته باشه یا این اسکریپت اول نباشه، رکورد اضافه کن
                if (tableExists || scripts.IndexOf(scriptPath) > 0)
                {
                    await _context.Set<AppliedScript>().AddAsync(new AppliedScript
                    {
                        ScriptName = Path.GetFileName(scriptPath),
                        MigrationName = migrationName,
                        AppliedOn = DateTime.UtcNow
                    }, ct);
                }
            }

            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
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
            // اصلاح خطا: استفاده از AsEnumerable().Reverse() برای حلقه foreach
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

        var psi = new ProcessStartInfo
        {
            FileName = _options.PgDumpPath ?? "pg_dump",
            Arguments = string.Join(" ", args),
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrEmpty(builder.Password))
            psi.Environment["PGPASSWORD"] = builder.Password;

        using var p = Process.Start(psi)!;
        var error = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"pg_dump failed: {error}");
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

            // ایجاد فایل برای نوشتن
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            await using var fileWriter = new StreamWriter(fileStream, Encoding.UTF8);

            // شروع عملیات اکسپورت متنی از دیتابیس
            using var reader = await conn.BeginTextExportAsync($"COPY {table} TO STDOUT WITH (FORMAT CSV, HEADER)", ct);

            // انتقال داده‌ها به صورت تکه تکه (Chunk) از دیتابیس به فایل
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
        // تمام عملیات باید روی همین یک کانکشن انجام شود تا جدول موقت دیده شود
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        foreach (var file in Directory.GetFiles(_options.ExportsFolder, "export_*.csv"))
        {
            var table = Path.GetFileNameWithoutExtension(file).Split('_').Last();
            var tempTable = $"temp_{Guid.NewGuid():N}".Substring(0, 10);

            // 1. ایجاد جدول موقت روی همین کانکشن
            await using (var cmd = new NpgsqlCommand($"CREATE TEMP TABLE {tempTable} (LIKE {table} INCLUDING DEFAULTS)", conn))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // 2. کپی داده‌ها از فایل CSV به جدول موقت
            using var fileStream = File.OpenRead(file);
            using var fileReader = new StreamReader(fileStream, Encoding.UTF8);
            
            // شروع عملیات ایمپورت متنی به دیتابیس
            await using (var writer = await conn.BeginTextImportAsync($"COPY {tempTable} FROM STDIN WITH (FORMAT CSV, HEADER)", ct))
            {
                var buffer = new char[8192];
                int charsRead;
                while ((charsRead = await fileReader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await writer.WriteAsync(buffer, 0, charsRead);
                }
                // پایان بلاک using باعث بسته شدن writer و کامیت شدن COPY می‌شود
            }

            // 3. انتقال از جدول موقت به اصلی
            await using (var cmd = new NpgsqlCommand($"INSERT INTO {table} SELECT * FROM {tempTable} ON CONFLICT (id) DO NOTHING", conn))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
    }
}
