using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.IO;

namespace KSDbMigrator;

public class DbMigrator<TContext> : IDbMigrator where TContext : DbContext
{
    private readonly TContext _context;
    private readonly KSDbMigratorOptions _options;
    private readonly IWebHostEnvironment _env;

    public DbMigrator(TContext context, KSDbMigratorOptions options, IWebHostEnvironment env)
    {
        _context = context;
        _options = options;
        _env = env;
    }

    public Task ApplyPendingScriptsAsync(CancellationToken ct = default)
    {
        // پیاده‌سازی واقعی بعداً اضافه میشه
        return Task.CompletedTask;
    }

    public Task RollbackToMigrationAsync(string targetMigrationName, CancellationToken ct = default)
    {
        // پیاده‌سازی واقعی بعداً
        return Task.CompletedTask;
    }
}
