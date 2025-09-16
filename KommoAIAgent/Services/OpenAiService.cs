using KommoAIAgent.Helpers;
using KommoAIAgent.Services.Interfaces;
using OpenAI; 
using OpenAI.Chat;
using System.ClientModel;
using System.Reflection;


namespace KommoAIAgent.Services;

/// <summary>
/// Implementación del servicio de IA usando la NUEVA LIBRERÍA OFICIAL de OpenAI.
/// </summary>
public class OpenAiService : IAiService
{
    private readonly OpenAIClient _client;
    private readonly ILogger<OpenAiService> _logger;
    private readonly IConfiguration _cfg;

    // El constructor recibe IConfiguration para poder leer la ApiKey desde appsettings.json
    public OpenAiService(IConfiguration configuration, ILogger<OpenAiService> logger, IConfiguration cfg)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentNullException(nameof(apiKey), "La ApiKey de OpenAI no está configurada en appsettings.json");
        }

        // Creamos el cliente oficial de OpenAI.
        _client = new OpenAIClient(apiKey);
        _logger = logger;
        _cfg = cfg;
    }

    /// <summary>
    /// Cumple con el contrato de IAiService. Genera una respuesta usando la librería oficial.
    /// </summary>
    public async Task<string> GenerateContextualResponseAsync(string textPrompt)
    {
        _logger.LogInformation("Enviando prompt a OpenAI (con la librería oficial...");

        try
        {
            // La lista de mensajes ahora se construye con clases diferentes.
            ChatMessage[] messages =
            {
            ChatMessage.CreateSystemMessage("Eres un asistente virtual experto y amigable. Responde de forma concisa y profesional."),
            ChatMessage.CreateUserMessage(textPrompt)
             };

            var model = _cfg["OpenAI:Model"] ?? "gpt-4o-mini";
            var maxTok = int.TryParse(_cfg["OpenAI:MaxOutputTokens"], out var v) ? v : 400;

            // La llamada a la API también es diferente.
            var chatClient = _client.GetChatClient(model);
            ChatCompletion response = await chatClient.CompleteChatAsync(messages);
            var options = new ChatCompletionOptions { MaxOutputTokenCount = maxTok };


            var completion = await CallWithRetryAsync(
            () => chatClient.CompleteChatAsync(messages, options),
            _logger
             );

            var aiResponse = completion.Content?.FirstOrDefault()?.Text;
            _logger.LogInformation("Respuesta recibida de OpenAI exitosamente.");
            return aiResponse ?? "No se pudo obtener una respuesta.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió una excepción al comunicarse con la API oficial de OpenAI.");
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
        _logger.LogInformation("Enviando prompt de IMAGEN Y TEXTO a OpenAI (modelo GPT-4o)...");
        _logger.LogInformation("URL de la imagen: {ImageUrl}", Utils.MaskUrl(imageUrl));

        try
        {
            ChatMessage[] messages =
            [
            ChatMessage.CreateSystemMessage(
                "Eres un asistente virtual experto. Analiza la imagen proporcionada y responde de forma útil y concisa."
            ),
            ChatMessage.CreateUserMessage(
                ChatMessageContentPart.CreateTextPart(textPrompt),

                // URL pública/estable:
                ChatMessageContentPart.CreateImagePart(new Uri(imageUrl))
                // Si requiere auth/expira: ChatMessageContentPart.CreateImage(imageBytes, "image/png")
            )
        ];

            //Trae de la configuración de appsettings.json
            var model = _cfg["OpenAI:Model"] ?? "gpt-4o-mini";
            var maxTok = int.TryParse(_cfg["OpenAI:MaxOutputTokens"], out var v) ? v : 400;


            var chatClient = _client.GetChatClient(model); // _client: OpenAIClient inyectado
            var options = new ChatCompletionOptions { MaxOutputTokenCount = maxTok };


            var completion = await CallWithRetryAsync(
            () => chatClient.CompleteChatAsync(messages, options),
            _logger
             );


            var aiResponse = completion.Content.FirstOrDefault()?.Text;
            _logger.LogInformation("Respuesta de análisis de imagen recibida de OpenAI exitosamente.");
            return aiResponse ?? "No se pudo obtener una respuesta sobre la imagen.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ocurrió una excepción durante el análisis de imagen con OpenAI.");
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
        _logger.LogInformation("Enviando IMAGEN (bytes) + TEXTO a OpenAI (GPT-4o)…");

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

            var model = _cfg["OpenAI:Model"] ?? "gpt-4o-mini";
            var maxTok = int.TryParse(_cfg["OpenAI:MaxOutputTokens"], out var v) ? v : 400;
           
            var chatClient = _client.GetChatClient(model); // o gpt-4o-mini para abaratar
            var options = new ChatCompletionOptions { MaxOutputTokenCount = maxTok };

            var completion = await CallWithRetryAsync(
            () => chatClient.CompleteChatAsync(messages, options),
            _logger
            );


            var aiText = completion.Content?.FirstOrDefault()?.Text;
            return aiText ?? "No pude extraer información de la imagen.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción durante análisis de imagen con OpenAI.");
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
            var model = _cfg["OpenAI:Model"] ?? "gpt-4o-mini";
                        var chat = _client.GetChatClient(model);
            var messages = new[] { ChatMessage.CreateUserMessage("ping") };
            var res = await chat.CompleteChatAsync(messages, new ChatCompletionOptions { MaxOutputTokenCount = 3 });
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
            var mdl = model ?? _cfg["OpenAI:Model"] ?? "gpt-4o-mini";
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