using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KSDbMigrator.Tests;

public class MigratorTests
{
    [Fact]
    public void CanRegister()
    {
        var sc = new ServiceCollection();
        sc.AddDbContext<TestCtx>(o => o.UseInMemoryDatabase("test"));
        sc.AddKSDbMigrator<TestCtx>(_ => { });
        var sp = sc.BuildServiceProvider();
        Assert.NotNull(sp.GetService<IDbMigrator>());
    }
}

class TestCtx : DbContext
{
    public DbSet<AppliedScript> AppliedScripts => Set<AppliedScript>();
    public TestCtx(DbContextOptions<TestCtx> o) : base(o) { }
}
