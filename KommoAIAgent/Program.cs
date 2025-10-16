using KommoAIAgent.Api.Middleware;
using KommoAIAgent.Api.Security;
using KommoAIAgent.Application.Interfaces;
using KommoAIAgent.Domain.Tenancy;
using KommoAIAgent.Infrastructure.Caching;
using KommoAIAgent.Infrastructure.Knowledge;
using KommoAIAgent.Infrastructure.Kommo;
using KommoAIAgent.Infrastructure.Persistence;
using KommoAIAgent.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector.Npgsql;


var builder = WebApplication.CreateBuilder(args);


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



// -------------------- Servicios App --------------------

//Servicio de configuración de proveedor de IA y su apiKey tanto por tenat como estándar.
builder.Services.AddSingleton<IAiCredentialProvider, AiCredentialProvider>();
builder.Services.AddScoped<IAiService, OpenAiService>();       // IA multi-tenant
builder.Services.AddScoped<OpenAiService>();
builder.Services.AddScoped<IWebhookHandler, WebhookHandler>();

// Configura el HttpClient de KommoApiService leyendo BaseUrl/Token del tenant por request.
builder.Services.AddHttpClient<IKommoApiService, KommoApiService>()
     .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
     {
         // Reduce el tiempo de vida del pool para “resetear” conexiones con frecuencia.
         PooledConnectionLifetime = TimeSpan.FromMinutes(1),
         // Si algún día se vuelve a HTTP/2 y notas coalescing, habilitar múltiples conexiones:
         // EnableMultipleHttp2Connections = true
     });


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


//API Básica
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// -------------------- CORS --------------------
var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>() ?? ["https://localhost:7000", "https://serticloud.com"];

// Política CORS para permitir solo los orígenes listados en configuración..incluyendo front
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();


// -------------------- Middleware --------------------
app.UseMiddleware<TenantResolutionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (ctx, next) =>
{
    var th = ctx.Request.Headers["X-Tenant-Slug"].ToString();
    var tq = ctx.Request.Query["tenant"].ToString();
    Console.WriteLine($"--> {ctx.Request.Method} {ctx.Request.Path} tenant(h)={th} tenant(q)={tq}");
    await next();
    Console.WriteLine($"<-- {ctx.Response.StatusCode} {ctx.Request.Path}");
});



// Logging scope por tenant en cada request (si viene en header)
app.Use(async (ctx, next) =>
{
    var reqId = ctx.TraceIdentifier;
    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();

    using (logger.BeginScope(new Dictionary<string, object?>
    {
        ["requestId"] = reqId
    }))
    {
        await next();
    }
});


app.UseHttpsRedirection();

app.UseStaticFiles();

// Redirige /admin a /admin/index.html
app.MapGet("/admin", ctx => {
ctx.Response.Redirect("/admin/index.html");
return Task.CompletedTask;
});

app.UseCors("AllowFrontend");

app.MapControllers();

app.Run();
