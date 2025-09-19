using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Helpers;
using KommoAIAgent.Services.Interfaces;
using OpenAI; 
using OpenAI.Chat;
using OpenAI.Realtime;
using System.ClientModel;
using System.Reflection;


namespace KommoAIAgent.Services;

/// <summary>
///Implementación del servicio de IA (SDK oficial OpenAI) con configuración por tenant.
/// </summary>
public class OpenAiService : IAiService
{
    private readonly OpenAIClient _client;
    private readonly ILogger<OpenAiService> _logger;
    private readonly ITenantContext _tenant;

    // El constructor recibe IConfiguration para poder leer la ApiKey desde appsettings.json
    public OpenAiService(ITenantContext tenant, ILogger<OpenAiService> logger)
    {
        _logger = logger;
        _tenant = tenant;

        var apiKey = _tenant.Config.OpenAI.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException($"OpenAI.ApiKey no configurada para tenant '{_tenant.CurrentTenantId}'");

        }

        // Creamos el cliente oficial de OpenAI.
        _client = new OpenAIClient(apiKey);
        
    }

    /// <summary>
    /// Genera una respuesta de texto a partir de un prompt de usuario.
    /// </summary>
    public async Task<string> GenerateContextualResponseAsync(string textPrompt)
    {
        var model = _tenant.Config.OpenAI.Model ?? "gpt-4o-mini";
        var maxTok = 400;
        _logger.LogInformation("↗️ OpenAI.Generate (tenant={Tenant}, model={Model})", _tenant.CurrentTenantId, model);

        try
        {
            // La lista de mensajes se construye con clases diferentes directo de la líbrería.
            ChatMessage[] messages =
            {
            ChatMessage.CreateSystemMessage("Eres un asistente virtual experto y amigable. Responde de forma concisa y profesional."),
            ChatMessage.CreateUserMessage(textPrompt)
             };

            var chatClient = _client.GetChatClient(model);
            var options = new ChatCompletionOptions { MaxOutputTokenCount = maxTok };

            // Llamada con retry en 429/503
            var completion = await CallWithRetryAsync(
            () => chatClient.CompleteChatAsync(messages, options),
            _logger
             );

            // Extrae la respuesta del primer choice
            var aiResponse = completion.Content?.FirstOrDefault()?.Text;
            _logger.LogInformation("Respuesta recibida de OpenAI exitosamente. (tenant={Tenant})", _tenant.CurrentTenantId);
            return aiResponse ?? "No se pudo obtener una respuesta.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió una excepción al comunicarse con la API oficial de OpenAI.(tenant={Tenant})", _tenant.CurrentTenantId);
            return "Hubo un error crítico al contactar al servicio de IA.";
        }
    }

    /// <summary>
    /// Analiza una imagen vía URL pública y responde con texto contextual.
    /// </summary>
    /// <param name="textPrompt"></param>
    /// <param name="imageUrl"></param>
    /// <returns></returns>
    public async Task<string> AnalyzeImageAndRespondAsync(string textPrompt, string imageUrl)
    {
        var model = _tenant.Config.OpenAI.Model ?? "gpt-4o-mini";
        var maxTok = 400;

        _logger.LogInformation("↗️ OpenAI.Vision-URL (tenant={Tenant}, model={Model}, url={Url})",
            _tenant.CurrentTenantId, model, Utils.MaskUrl(imageUrl));

        try
        {
            ChatMessage[] messages =
            [
                ChatMessage.CreateSystemMessage("Eres un asistente virtual experto. Analiza la imagen proporcionada y responde de forma útil y concisa."),
                    ChatMessage.CreateUserMessage(
                        ChatMessageContentPart.CreateTextPart(textPrompt ?? string.Empty),
                        ChatMessageContentPart.CreateImagePart(new Uri(imageUrl))   // URL pública
                    )
            ];

            var chatClient = _client.GetChatClient(model);
            var options = new ChatCompletionOptions { MaxOutputTokenCount = maxTok };

            var completion = await CallWithRetryAsync(
                () => chatClient.CompleteChatAsync(messages, options),
                _logger
            );

            var aiResponse = completion.Content.FirstOrDefault()?.Text;
            _logger.LogInformation("✅ OpenAI Vision OK (tenant={Tenant})", _tenant.CurrentTenantId);
            return aiResponse ?? "No se pudo obtener una respuesta sobre la imagen.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ OpenAI Vision error (tenant={Tenant})", _tenant.CurrentTenantId);
            return "Hubo un error crítico al analizar la imagen con la IA.";
        }
    }

    /// <summary>
    /// Analiza una imagen a partir de un arreglo de bytes.
    /// </summary>
    /// <param name="textPrompt"></param>
    /// <param name="imageBytes"></param>
    /// <param name="mimeType"></param>
    /// <returns></returns>
    public async Task<string> AnalyzeImageFromBytesAsync(string textPrompt, byte[] imageBytes, string mimeType)
    {
        var model = _tenant.Config.OpenAI.Model ?? "gpt-4o-mini";
        var maxTok = 400;

        _logger.LogInformation("↗️ OpenAI.Vision-Bytes (tenant={Tenant}, model={Model}, mime={Mime})",
            _tenant.CurrentTenantId, model, mimeType);

        try
        {
            ChatMessage[] messages =
            [
                ChatMessage.CreateSystemMessage("Eres un analista de imágenes. Responde breve, claro y útil."),
                    ChatMessage.CreateUserMessage(
                        ChatMessageContentPart.CreateTextPart(textPrompt ?? "Describe la imagen y extrae cualquier texto visible."),
                        ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), mimeType)
                    )
            ];

            var chatClient = _client.GetChatClient(model);
            var options = new ChatCompletionOptions { MaxOutputTokenCount = maxTok };

            var completion = await CallWithRetryAsync(
                () => chatClient.CompleteChatAsync(messages, options),
                _logger
            );

            var aiText = completion.Content?.FirstOrDefault()?.Text;
            _logger.LogInformation("✅ OpenAI Vision BYTES OK (tenant={Tenant})", _tenant.CurrentTenantId);
            return aiText ?? "No pude extraer información de la imagen.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ OpenAI Vision BYTES error (tenant={Tenant})", _tenant.CurrentTenantId);
            return "⚠️ No pude analizar la imagen por ahora. Un agente te ayudará enseguida.";
        }
    }

    /// <summary>
    /// Envía un "ping" mensaje al cliente chat IA y determina si la respuesta fue recibida.
    /// </summary>    
    /// <see langword="false"/>.</remarks>
    /// <returns><see langword="true"/>
    public async Task<bool> PingAsync()
    {
        try
        {
            var model = _tenant.Config.OpenAI.Model ?? "gpt-4o-mini";
            var chat = _client.GetChatClient(model);
            var res = await chat.CompleteChatAsync(new[] { ChatMessage.CreateUserMessage("ping") },
            new ChatCompletionOptions { MaxOutputTokenCount = 3 });

            // Si obtenemos cualquier contenido, consideramos que está OK.
            return res.Value.Content?.Any() == true;
        }
        catch
        {
            return false;
        }
    }


    /// <summary>
    /// Helper para retry: 3 intentos en 429/503 o error de red.
    /// </summary>
    /// <param name="call"></param>
    /// <param name="logger"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    private static async Task<ChatCompletion> CallWithRetryAsync(
        Func<Task<ClientResult<ChatCompletion>>> call,
    ILogger logger,
    CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var res = await call();
                return res.Value; // ChatCompletion
            }
            catch (ClientResultException ex) when ((ex.Status == 429 || ex.Status == 503) && attempt < 3) // del SDK oficial
            {
                // Lee Retry - After de forma segura
                string? retryAfter = null;
                var raw = ex.GetRawResponse();
                raw?.Headers.TryGetValue("retry-after", out retryAfter);

                var delay = int.TryParse(retryAfter, out var seconds)
                    ? TimeSpan.FromSeconds(seconds)
                    : TimeSpan.FromMilliseconds(300 * attempt + Random.Shared.Next(0, 300));

                logger.LogWarning(ex, "OpenAI {Status}. Retry {Attempt} en {Delay}.", ex.Status, attempt, delay);
                await Task.Delay(delay, ct);
            }
        catch (HttpRequestException ex) when(attempt < 3)
        {
            var delay = TimeSpan.FromMilliseconds(300 * attempt + Random.Shared.Next(0, 300));
            logger.LogWarning(ex, "Error de red hacia OpenAI. Retry {Attempt} en {Delay}.", attempt, delay);
            await Task.Delay(delay, ct);
        }
    }
        // Último intento sin catch para propagar error si falla nuevamente
        return (await call()).Value;
    }

    /// <summary>
    /// Genera una respuesta completa a partir de una lista de mensajes (con roles), para historial dentro de la misma conversación.
    /// </summary>
    /// <param name="messages"></param>
    /// <param name="maxTokens"></param>
    /// <param name="model"></param>
    /// <returns></returns>
    public async Task<string> CompleteAsync(IEnumerable<ChatMessage> messages, int maxTokens = 400, string? model = null)
    {
        try
        {
            var mdl = model ?? _tenant.Config.OpenAI.Model ?? "gpt-4o-mini";
            var chatClient = _client.GetChatClient(mdl);
            var options = new ChatCompletionOptions { MaxOutputTokenCount = maxTokens };

            var completion = await CallWithRetryAsync(
                () => chatClient.CompleteChatAsync(messages, options),
                _logger
            );

            return completion.Content?.FirstOrDefault()?.Text ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en CompleteAsync");
            return "";
        }
    }

}