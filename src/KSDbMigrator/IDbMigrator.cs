using System.Threading;
using System.Threading.Tasks;

namespace KSDbMigrator;

public interface IDbMigrator
{
    Task ApplyPendingScriptsAsync(CancellationToken ct = default);
    Task RollbackToMigrationAsync(string targetMigrationName, CancellationToken ct = default);
}
