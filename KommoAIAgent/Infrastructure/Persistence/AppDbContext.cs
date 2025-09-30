using KommoAIAgent.Domain.Ia;
using KommoAIAgent.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KommoAIAgent.Infrastructure.Persistence
{
    /// <summary>
    /// DbContext principal para auditoría IA, consumo y catálogo de tenants.
    /// Guarda solo info "IA-relevante" (no fotos ni conversación completa).
    /// </summary>
    public sealed class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<IaLog> IaLogs => Set<IaLog>();
        public DbSet<IaMetric> IaMetrics => Set<IaMetric>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            // Converter VO <-> string (slug normalizado)
            var tenantIdConverter = new ValueConverter<TenantId, string>(
                toProvider => toProvider.Value,
                fromProvider => TenantId.From(fromProvider)
            );

            // === IaLog ===
            b.Entity<IaLog>(e =>
            {
                e.ToTable("ia_logs");
                e.HasKey(x => x.Id);

                e.Property(x => x.TenantId)
                 .HasConversion(tenantIdConverter)
                 .HasMaxLength(100)   // slug corto: index eficiente
                 .IsRequired();

                e.HasIndex(x => x.TenantId);
                e.Property(x => x.Model).IsRequired().HasMaxLength(120);
                e.Property(x => x.Input).IsRequired();
                e.Property(x => x.Output).IsRequired();
                e.Property(x => x.Meta);
                e.Property(x => x.CreatedAt).IsRequired();
                e.Property(x => x.UpdatedAt);
            });

            // === IaMetric ===
            b.Entity<IaMetric>(e =>
            {
                e.ToTable("ia_metrics");
                e.HasKey(x => x.Id);

                e.Property(x => x.TenantId)
                 .HasConversion(tenantIdConverter)
                 .HasMaxLength(100)
                 .IsRequired();

                e.HasIndex(x => x.TenantId);
                e.Property(x => x.Model).IsRequired().HasMaxLength(120);
                e.Property(x => x.PromptTokens).IsRequired();
                e.Property(x => x.CompletionTokens).IsRequired();
                e.Property(x => x.At).IsRequired();
                e.Property(x => x.CreatedAt).IsRequired();
                e.Property(x => x.UpdatedAt);
            });

            base.OnModelCreating(b);
        }
    }
}
