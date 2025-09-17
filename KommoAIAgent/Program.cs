using KommoAIAgent.Helpers;
using KommoAIAgent.Services;
using KommoAIAgent.Services.Interfaces;


var builder = WebApplication.CreateBuilder(args);


// --- 1. Configuración de Servicios (Contenedor de Inyección de Dependencias) ---

// Añade los servicios básicos para una API web y para la documentación con Swagger.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Aquí registramos nuestros servicios personalizados.
// Usamos "AddScoped" que significa que se creará una nueva instancia de estos servicios para cada petición web.

// Registra nuestro servicio de IA.
builder.Services.AddScoped<IAiService, OpenAiService>();

// Registra nuestro servicio orquestador.
builder.Services.AddScoped<IWebhookHandler, WebhookHandler>();

// Esta es la forma moderna y correcta de registrar servicios que usan HttpClient.
// No solo registra KommoApiService, sino que también gestiona el HttpClient de forma eficiente.
builder.Services.AddHttpClient<IKommoApiService, KommoApiService>();

//Registra nuestro almacén de memoria de conversaciones.
builder.Services.AddSingleton<IChatMemoryStore, InMemoryChatMemoryStore>();  // NUEVO

// Añade soporte para caché en memoria, útil para almacenar tokens u otros datos temporales.
builder.Services.AddMemoryCache();

// Registramos un buffer de mensajes en memoria para la ventana de envío de mensaes.
builder.Services.Configure<Microsoft.Extensions.Options.OptionsWrapper<object>>((_) => { }); // no es necesario realmente, solo asegurarte que IConfiguration está
builder.Services.AddSingleton<IMessageBuffer, InMemoryMessageBuffer>();

// Registramos el servicio para manejar la última imagen por chat.
builder.Services.AddSingleton<LastImageCache>();

var app = builder.Build();


// Configure the HTTP request pipeline.

// Habilitamos Swagger solo cuando estamos desarrollando. 
// Esto nos da una página útil para probar nuestra API si lo necesitamos.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


// Punto de entrada para verificar health check de la IA.
app.MapGet("/health/ai", async (IAiService ai) =>
{
    var ok = await ai.PingAsync();
    return ok ? Results.Ok(new { ai = "ok" }) : Results.StatusCode(503);
});


// Redirige las peticiones HTTP a HTTPS.
app.UseHttpsRedirection();

// Habilita el sistema para que las peticiones lleguen a nuestros controladores.
app.MapControllers();

app.Run();
