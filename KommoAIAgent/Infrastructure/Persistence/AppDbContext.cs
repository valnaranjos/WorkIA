using KommoAIAgent.Application.Tenancy;
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
        private readonly ITenantContext _tenantContext;

        public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options)
        {
            _tenantContext = tenantContext;
        }

        public DbSet<IaLog> IaLogs => Set<IaLog>();
        public DbSet<IaMetric> IaMetrics => Set<IaMetric>();
        protected override void OnModelCreating(ModelBuilder b)
        {
            // Converter: TenantId (VO con slug) <-> string en Postgres
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
                 .HasMaxLength(100)
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

            // === Filtro global multi-tenant ===
            // Solo aplica si hay tenant resuelto (slug no vacío); evita interferir en migraciones/boot.
            var currentSlug = _tenantContext.CurrentTenantId.Value;
            if (!string.IsNullOrWhiteSpace(currentSlug))
            {
                b.Entity<IaLog>().HasQueryFilter(x => x.TenantId.Value == currentSlug);
                b.Entity<IaMetric>().HasQueryFilter(x => x.TenantId.Value == currentSlug);
            }

            base.OnModelCreating(b);
        }
    }
}
