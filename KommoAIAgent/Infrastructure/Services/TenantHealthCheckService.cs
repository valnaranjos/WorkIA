using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using KommoAIAgent.Infrastructure.Persistence;
using KommoAIAgent.Application.Interfaces;

namespace KommoAIAgent.Infrastructure.Services;

public sealed class TenantHealthCheckService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<TenantHealthCheckService> _logger;

    public TenantHealthCheckService(IServiceProvider sp, ILogger<TenantHealthCheckService> logger)
    {
        _sp = sp; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Primer chequeo tras inicio
        await RunOnce(stoppingToken);

        // Cada 24h
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            await RunOnce(stoppingToken);
        }
    }

    private async Task RunOnce(CancellationToken ct)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ai = scope.ServiceProvider.GetRequiredService<IAiService>();
            var tenants = await db.Tenants.Where(t => t.IsActive).ToListAsync(ct);

            foreach (var t in tenants)
            {
                try
                {
                    // ping IA (rápido y barato)
                    await ai.PingAsync();

                    // TODO: ping Kommo si quieres (GET /api/v4/account) con KommoApiService
                    // await kommo.PingAsync(t.Slug, ct);

                    _logger.LogInformation("Health OK tenant={Slug}", t.Slug);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Health FAIL tenant={Slug}", t.Slug);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HealthCheck global error");
        }
    }
}
