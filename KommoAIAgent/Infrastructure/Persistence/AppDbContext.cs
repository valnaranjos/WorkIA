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

        // DbSets
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<IaLog> IaLogs => Set<IaLog>();
        public DbSet<IaMetric> IaMetrics => Set<IaMetric>();


        protected override void OnModelCreating(ModelBuilder b)
        {
            // Converter: TenantId (VO con slug) <-> string en Postgres
            var tenantIdConverter = new ValueConverter<TenantId, string>(
                toProvider => toProvider.Value,
                fromProvider => TenantId.From(fromProvider)
            );

            // === Tenant ===
            // Catálogo de tenants y su configuración.
            b.Entity<Tenant>(e =>
            {
                e.ToTable("tenants");
                e.HasKey(x => x.Id);

                // Identidad y ruteo
                e.Property(x => x.Slug).IsRequired().HasMaxLength(100);
                e.HasIndex(x => x.Slug).IsUnique();
                e.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
                e.Property(x => x.IsActive).HasDefaultValue(true);

                // Kommo
                e.Property(x => x.KommoBaseUrl).IsRequired().HasMaxLength(200);

                // IA
                e.Property(x => x.IaProvider).IsRequired().HasMaxLength(30);
                e.Property(x => x.IaModel).IsRequired().HasMaxLength(120);
                e.Property(x => x.Temperature);
                e.Property(x => x.TopP);
                e.Property(x => x.MaxTokens);

                // Budget & guardrails
                e.Property(x => x.MonthlyTokenBudget).HasDefaultValue(5_000_000);
                e.Property(x => x.AlertThresholdPct).HasDefaultValue(75);

                // Runtime defaults
                e.Property(x => x.MemoryTTLMinutes).HasDefaultValue(120);
                e.Property(x => x.ImageCacheTTLMinutes).HasDefaultValue(3);
                e.Property(x => x.DebounceMs).HasDefaultValue(700);
                e.Property(x => x.RatePerMinute).HasDefaultValue(15);
                e.Property(x => x.RatePer5Minutes).HasDefaultValue(60);

                // Auditoría
                e.Property(x => x.CreatedAt).IsRequired();
                e.Property(x => x.UpdatedAt);
            });

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
