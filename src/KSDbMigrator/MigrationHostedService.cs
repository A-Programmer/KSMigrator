using Microsoft.Extensions.Hosting;

namespace KSDbMigrator;

public class MigrationHostedService : IHostedService
{
    private readonly IDbMigrator _migrator;

    public MigrationHostedService(IDbMigrator migrator)
    {
        _migrator = migrator;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _migrator.ApplyPendingScriptsAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
