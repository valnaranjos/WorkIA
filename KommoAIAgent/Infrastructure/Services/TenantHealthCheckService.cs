using KommoAIAgent.Application.Interfaces;
using KommoAIAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KommoAIAgent.Infrastructure.Services;

public sealed class TenantHealthCheckService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<TenantHealthCheckService> _logger;
    public TenantHealthCheckService(IServiceProvider sp, ILogger<TenantHealthCheckService> logger)
    { _sp = sp; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tenants = await db.Tenants.Where(t => t.IsActive).ToListAsync(stoppingToken);

                foreach (var t in tenants)
                {
                    // TODO: prueba corta a Kommo (GET perfil) y a OpenAI (models list o tiny embed)
                    _logger.LogInformation("HealthCheck tenant={Slug} OK (placeholder)", t.Slug);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tenant health check error");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
