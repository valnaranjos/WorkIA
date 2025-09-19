using KommoAIAgent.Api.Middleware;
using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Helpers;
using KommoAIAgent.Infraestructure.Tenancy;
using KommoAIAgent.Services;
using KommoAIAgent.Services.Interfaces;


var builder = WebApplication.CreateBuilder(args);


//Configuración de Servicios- Contenedor de Inyección de Dependencias ---

// Añade los servicios básicos para una API web y para la documentación con Swagger.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// -------------------- MultiTenancy: opciones + servicios --------------------
// Vincula "MultiTenancy" del appsettings.json -> MultiTenancyOptions (usado por JsonTenantConfigProvider)
builder.Services.Configure<MultiTenancyOptions>(
    builder.Configuration.GetSection("MultiTenancy")
);

// Proveedor de config por tenant, resolver de tenant y accessor del contexto
builder.Services.AddSingleton<ITenantConfigProvider, JsonTenantConfigProvider>();
builder.Services.AddSingleton<ITenantResolver, TenantResolver>();
builder.Services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ITenantContextAccessor>().Current);

// -------------------- IA y Webhook --------------------
builder.Services.AddScoped<IAiService, OpenAiService>();   // OpenAiService ya adaptado a multi-tenant (lazy)
builder.Services.AddScoped<IWebhookHandler, WebhookHandler>();


// -------------------- Kommo HTTP Client (typed) por tenant --------------------
// Configura el HttpClient de KommoApiService leyendo BaseUrl/Token del tenant por request.
builder.Services.AddHttpClient<IKommoApiService, KommoApiService>();

//Servicio de almacén de memoria de conversaciones.
builder.Services.AddSingleton<IChatMemoryStore, InMemoryChatMemoryStore>();

// Soporte para caché en memoria, datos temporales e historial dentro de conversaciones.
builder.Services.AddMemoryCache();

// Buffer de mensajes en memoria para la ventana de envío de mensajes.
builder.Services.AddSingleton<IMessageBuffer, InMemoryMessageBuffer>();

// Servicio para manejar la última imagen por chat.
builder.Services.AddSingleton<LastImageCache>();

var app = builder.Build();


// HTTP request pipeline.

// Habilitamos Swagger solo para DESARROLLO. 
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//Middleware 
app.UseMiddleware<TenantResolutionMiddleware>();

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
