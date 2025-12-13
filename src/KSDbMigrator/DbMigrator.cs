using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace KSDbMigrator;

public class DbMigrator<TContext> : IDbMigrator where TContext : DbContext
{
    private readonly TContext _context;
    private readonly KSDbMigratorOptions _options;

    // IWebHostEnvironment حذف شد!
    public DbMigrator(TContext context, KSDbMigratorOptions options)
    {
        _context = context;
        _options = options;

        // حالا مسیرها رو مستقیم از options می‌گیریم
        Directory.CreateDirectory(_options.ApplyScriptsFolder);
        Directory.CreateDirectory(_options.RollbackScriptsFolder);
        Directory.CreateDirectory(_options.BackupsFolder);
        Directory.CreateDirectory(_options.ExportsFolder);
    }

    public Task ApplyPendingScriptsAsync(CancellationToken ct = default)
    {
        // پیاده‌سازی واقعی بعداً
        return Task.CompletedTask;
    }

    public Task RollbackToMigrationAsync(string targetMigrationName, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
