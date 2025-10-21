using KommoAIAgent.Api.Middleware;
using KommoAIAgent.Api.Security;
using KommoAIAgent.Application.Connectors;
using KommoAIAgent.Application.Interfaces;
using KommoAIAgent.Domain.Tenancy;
using KommoAIAgent.Infrastructure.Caching;
using KommoAIAgent.Infrastructure.Connectors;
using KommoAIAgent.Infrastructure.Knowledge;
using KommoAIAgent.Infrastructure.Kommo;
using KommoAIAgent.Infrastructure.Persistence;
using KommoAIAgent.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector.Npgsql;


var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("https://localhost:7000"); // solo en dev local
}


// -------------------- MultiTenancy --------------------
builder.Services.Configure<MultiTenancyOptions>(
    builder.Configuration.GetSection("MultiTenancy"));

builder.Services.AddSingleton<TenantContextAccessor>();
builder.Services.AddSingleton<ITenantContextAccessor>(sp => sp.GetRequiredService<TenantContextAccessor>());
builder.Services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>());


builder.Services.AddSingleton<ITenantResolver, TenantResolver>();
builder.Services.AddSingleton<ITenantConfigProvider, DbTenantConfigProvider>();


// -------------------- EF Core / PostgreSQL --------------------
builder.Services.AddScoped<AuditSaveChangesInterceptor>();


// DataSource compartido con pgvector (UNA sola vez)
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var conn = cfg.GetConnectionString("Postgres"); // usa la misma clave en toda la app
    var dsb = new NpgsqlDataSourceBuilder(conn);
    dsb.UseVector(); // habilita pgvector en este data source
    return dsb.Build(); // NpgsqlDataSource
});

//Conexión a la base de datos PostgreSQL con EF Core y el interceptor de auditoría.
builder.Services.AddDbContext<AppDbContext>((sp, opts) =>
{
    var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
    opts.UseNpgsql(dataSource);
    opts.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
    AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
});

//HTTSP CLIENTS!
// Configura el HttpClient de KommoApiService leyendo BaseUrl/Token del tenant por request.
builder.Services.AddHttpClient<IKommoApiService, KommoApiService>()
     .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
     {
         // Reduce el tiempo de vida del pool para “resetear” conexiones con frecuencia.
         PooledConnectionLifetime = TimeSpan.FromMinutes(1),
         // Si algún día se vuelve a HTTP/2 y notas coalescing, habilitar múltiples conexiones:
         // EnableMultipleHttp2Connections = true
     });

// -------------------- Servicios App --------------------

//Servicio de configuración de proveedor de IA y su apiKey tanto por tenat como estándar.
builder.Services.AddSingleton<IAiCredentialProvider, AiCredentialProvider>();
builder.Services.AddScoped<IAiService, OpenAiService>();       // IA multi-tenant
builder.Services.AddScoped<OpenAiService>();
builder.Services.AddScoped<IWebhookHandler, WebhookHandler>();



// Factory genérico para conectores externos (reutilizable), sin tipos genéricos) crea un factory genérico que PostgresConnectorFactory usa para crear clientes HTTP dinámicos por conector.
builder.Services.AddHttpClient(); // 🆕 Este es para la factory de conectores

// Soporte para caché en memoria, datos temporales e historial dentro de conversaciones.
builder.Services.AddMemoryCache(options =>
{
    //Compactar cuando se alcance 80% del límite (libera 20%)
    options.CompactionPercentage = 0.20;

    //Intervalo de escaneo para expiración de items (default: 1 min)
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(1);
});
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

// RAG (fase 2: BD postgres + pgvector)
builder.Services.AddScoped<IKnowledgeStore, PgVectorKnowledgeStore>();
//Servicio de caché de embeddings en PostgreSQL (pgvector)
builder.Services.AddScoped<IEmbeddingCache, PostgresEmbeddingCache>();

// Proveedor concreto (hoy OpenAI)
builder.Services.AddScoped<IEmbeddingProvider, OpenAIEmbeddingProvider>();

// Servicio de embeddings que usa doble caché (mem + DB) y el provider
builder.Services.AddScoped<IEmbedder, OpenAIEmbeddingService>();

// Servicio de tracking de uso de IA en PostgreSQL
builder.Services.AddScoped<IAIUsageTracker, PostgresAIUsageTracker>();

//Servicio de api key para administración.
builder.Services.AddScoped<AdminApiKeyFilter>();

//Servicio de RAG (Recuperación Augmentada por IA) que usa el KnowledgeStore y el Embedder, para separación del webhookHandler
builder.Services.AddScoped<IRagRetriever, RagRetriever>();

//Servicio de catálogo de costes de IA leyendo desde tabla ia_costs en PostgreSQL.
builder.Services.AddSingleton<IAiCostCatalog, PostgresAiCostCatalog>();

// Servicio de chequeo de salud de tenants en segundo plano, para detectar problemas de configuración en producción.
builder.Services.AddHostedService<TenantHealthCheckService>();


// -------------------- Conectores Externos (Multi-Tenant) --------------------
builder.Services.AddScoped<IConnectorFactory, PostgresConnectorFactory>(); // Factory de conectores desde PostgreSQL
builder.Services.AddScoped<IIntentDetector, LlmIntentDetector>(); // Detector de intenciones basado en LLM


//API Básica
builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// -------------------- CORS --------------------
var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:5174", "https://localhost:7000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Si usas cookies/auth
    });
});

var app = builder.Build();

//Soporte para X-Forwarded-* headers (ALB/CloudFront)
if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                         | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
    });
}


app.UseDefaultFiles();

app.UseStaticFiles();

// -------------------- Middleware --------------------
app.UseMiddleware<TenantResolutionMiddleware>();

// Swagger solo en desarrollo
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Logging middleware con info más útil
app.Use(async (ctx, next) =>
{
    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    var start = DateTime.UtcNow;

    var tenantHeader = ctx.Request.Headers["X-Tenant-Slug"].ToString();
    var tenantQuery = ctx.Request.Query["tenant"].ToString();
    var tenant = tenantHeader ?? tenantQuery ?? "(none)";

    logger.LogInformation(
        "→ {Method} {Path} tenant={Tenant}",
        ctx.Request.Method, ctx.Request.Path, tenant
    );

    await next();

    var elapsed = DateTime.UtcNow - start;
    logger.LogInformation(
        "← {Status} {Path} {ElapsedMs}ms",
        ctx.Response.StatusCode, ctx.Request.Path, elapsed.TotalMilliseconds
    );
});


// Request ID scope
app.Use(async (ctx, next) =>
{
    var reqId = ctx.TraceIdentifier;
    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();

    using (logger.BeginScope(new Dictionary<string, object?>
    {
        ["requestId"] = reqId,
        ["path"] = ctx.Request.Path.ToString()
    }))
    {
        await next();
    }
});

//UseHttpsRedirection solo en dev (en AWS el ALB maneja HTTPS)
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}


app.UseCors("AllowFrontend");

app.MapControllers();

app.MapFallbackToFile("/index.html");

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation(
    "KommoAIAgent iniciado - Environment: {Env}",
    app.Environment.EnvironmentName
);

app.Run();
