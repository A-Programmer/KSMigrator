using Microsoft.EntityFrameworkCore;

namespace KSDbMigrator;

public class AppliedScript
{
    public int Id { get; set; }
    public string ScriptName { get; set; } = string.Empty;
    public string MigrationName { get; set; } = string.Empty;
    public DateTime AppliedOn { get; set; }
}

public static class ModelBuilderExtensions
{
    public static void AddAppliedScript(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppliedScript>(entity =>
        {
            entity.ToTable("applied_scripts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ScriptName).HasMaxLength(255);
            entity.Property(e => e.MigrationName).HasMaxLength(255);
            entity.Property(e => e.AppliedOn).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
    }
}
