using KSDbMigrator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal class MigrationHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MigrationHostedService> _logger;

    public MigrationHostedService(IServiceProvider serviceProvider, ILogger<MigrationHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<IDbMigrator>();

        try
        {
            _logger.LogInformation("Starting auto migration on startup...");
            await migrator.ApplyPendingScriptsAsync(cancellationToken);
            _logger.LogInformation("Auto migration completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during auto migration on startup. Application will continue.");
            // برنامه ادامه می‌ده — متوقف نمی‌شه
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
