using KommoAIAgent.Api.Middleware;
using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Infraestructure.Tenancy;
using KommoAIAgent.Infrastructure;
using KommoAIAgent.Infrastructure.Persistence;
using KommoAIAgent.Infrastructure.Tenancy;
using KommoAIAgent.Services;
using KommoAIAgent.Services.Interfaces;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

// -------------------- MultiTenancy --------------------
builder.Services.Configure<MultiTenancyOptions>(
    builder.Configuration.GetSection("MultiTenancy"));

builder.Services.AddSingleton<ITenantResolver, TenantResolver>();
builder.Services.AddSingleton<ITenantConfigProvider, JsonTenantConfigProvider>();
builder.Services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
builder.Services.AddScoped<ITenantContext>(sp =>
    sp.GetRequiredService<ITenantContextAccessor>().Current);

// -------------------- EF Core / PostgreSQL --------------------
builder.Services.AddScoped<AuditSaveChangesInterceptor>();

//Conexión a la base de datos PostgreSQL con EF Core y el interceptor de auditoría
builder.Services.AddDbContext<AppDbContext>((sp, opts) =>
{
    var conn = builder.Configuration.GetConnectionString("Postgres");
    opts.UseNpgsql(conn);
    opts.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
    //Ajuste de compatibilidad de timestamps si te hace falta
    AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
});
// -------------------- Servicios App --------------------
builder.Services.AddScoped<IAiService, OpenAiService>();       // IA multi-tenant
builder.Services.AddScoped<IWebhookHandler, WebhookHandler>();

// Configura el HttpClient de KommoApiService leyendo BaseUrl/Token del tenant por request.
builder.Services.AddHttpClient<IKommoApiService, KommoApiService>();


// Soporte para caché en memoria, datos temporales e historial dentro de conversaciones.
builder.Services.AddMemoryCache();
// Buffer de mensajes en memoria para la ventana de envío de mensajes.
builder.Services.AddSingleton<IMessageBuffer, InMemoryMessageBuffer>();
// Servicio para manejar la última imagen por chat.
builder.Services.AddSingleton<LastImageCache>();
// Servicio de memoria conversacional (Redis con fallback a local)
builder.Services.AddSingleton<IChatMemoryStore, RedisChatMemoryStore>();
// Rate limiter por tenant (in-memory)
builder.Services.AddSingleton<IRateLimiter, InMemoryRateLimiter>();

// Presupuesto de tokens por periodo (Daily/Monthly) en memoria por tenant.
builder.Services.AddSingleton<ITokenBudget, InMemoryPeriodicTokenBudget>();



//API Básica
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();


// -------------------- Middleware --------------------
app.UseMiddleware<TenantResolutionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}



// -------------------- Endpoints --------------------

// Punto de entrada para verificar health check de la IA.
app.MapGet("/health/ai", async (IAiService ai) =>
{
    var ok = await ai.PingAsync();
    return ok ? Results.Ok(new { ai = "ok" }) : Results.StatusCode(503);
});

//Diagnóstico de tenant actual
app.MapGet("/__whoami", (ITenantContext t) =>
    Results.Json(new { tenant = t.CurrentTenantId.Value, baseUrl = t.Config.Kommo.BaseUrl })
);


app.UseHttpsRedirection();

app.MapControllers();

app.Run();
