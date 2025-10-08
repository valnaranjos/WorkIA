using KommoAIAgent.Application.Common;
using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Services.Interfaces;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using System.ClientModel;
using System.Reflection;
using System.Text.Json;

namespace KommoAIAgent.Services;

/// <summary>
/// Implementación del servicio de IA usando el SDK oficial de OpenAI.
/// - Multi-tenant: lee ApiKey, modelo y parámetros desde ITenantContext.
/// - Retry automático en 429/503 con backoff exponencial.
/// - Soporta texto simple, análisis de imágenes (URL y bytes) e historial conversacional.
/// - Soporta generación de embeddings para vectores.
/// - Tracking de tokens con fallback de estimación
/// </summary>
public class OpenAiService : IAiService
{
    private OpenAIClient? _client;
    private readonly ILogger<OpenAiService> _logger;
    private readonly ITenantContext _tenant;
    private readonly IAiCredentialProvider _keys;
    private readonly IAIUsageTracker _usage;

    private string? _cachedApiKey; // cache de ApiKey por tenant
    private readonly object _clientLock = new(); // lock para recreación del cliente

    //Estándar de embedding pequeño y rápido
    private const string DefaultEmbeddingModel = "text-embedding-3-small";


    // El constructor recibe IConfiguration para poder leer la ApiKey desde appsettings.json
    public OpenAiService(ITenantContext tenant,
        ILogger<OpenAiService> logger,
        IAiCredentialProvider keys,
        IAIUsageTracker usage)
    {
        _logger = logger;
        _tenant = tenant;
        _keys = keys;
        _usage = usage;      
    }

    /// <summary>
    /// Genera una respuesta de texto a partir de un prompt de usuario.
    /// - Usa SystemPrompt y BusinessRules del tenant si están configurados.
    /// - Aplica parámetros (temperature, topP, maxTokens) desde TenantConfig.
    /// </summary>
    public async Task<string> GenerateContextualResponseAsync(string textPrompt, CancellationToken ct = default)
    {
        var client = EnsureClient();
        var model = _tenant.Config?.OpenAI?.Model ?? "gpt-4o-mini";
        var maxTokens = _tenant.Config?.OpenAI?.MaxTokens ?? 400;

        //Usa el helper centralizado
        var messages = BuildSystemMessages();
        messages.Add(ChatMessage.CreateUserMessage(textPrompt ?? string.Empty));

        var chatClient = client.GetChatClient(model);
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = maxTokens,
            Temperature = (float)(_tenant.Config?.OpenAI?.Temperature ?? 0.7f),
            TopP = (float)(_tenant.Config?.OpenAI?.TopP ?? 1.0f)
        };

        var completion = await CallWithRetryAsync(
            () => chatClient.CompleteChatAsync(messages, options, cancellationToken: ct),
            _logger,
            ct
        );

        var assistantText = completion?.Content?.FirstOrDefault()?.Text ?? string.Empty;

        // MÉTRICAS (chat): usa AiUtil para DRY
        await TrackChatUsageAsync(
            textPrompt ?? string.Empty,
            assistantText,
            completion?.Usage?.InputTokenCount,
            completion?.Usage?.OutputTokenCount,
            ct);

        return assistantText;
    }


    /// <summary>
    /// Analiza una imagen desde un arreglo de bytes en memoria.
    /// - Usa SystemPrompt y BusinessRules del tenant.
    /// - Requiere especificar el MIME type.
    /// </summary>
    /// <param name="textPrompt"></param>
    /// <param name="imageBytes"></param>
    /// <param name="mimeType"></param>
    /// <returns></returns>
    public async Task<string> AnalyzeImageFromBytesAsync(string textPrompt, byte[] imageBytes, string mimeType)
    {
        var cfg = _tenant.Config;
        var model = cfg.OpenAI?.VisionModel ?? "gpt-4o";
        var maxTokens = cfg.OpenAI?.MaxTokens ?? 600;

        _logger.LogInformation(
        "OpenAI Vision (Bytes) → tenant={Tenant}, model={Model}, mime={Mime}, size={Size}KB",
        _tenant.CurrentTenantId, model, mimeType, imageBytes.Length / 1024);

        try
        {
            // Usa helper 
            var messages = BuildSystemMessages();

            // Usuario: texto + imagen en bytes
            messages.Add(
                ChatMessage.CreateUserMessage(
                    ChatMessageContentPart.CreateTextPart(
                        textPrompt ?? "Describe la imagen y extrae cualquier texto visible."
                    ),
                    ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), mimeType)
                )
            );

            var chatClient = _client.GetChatClient(model);
            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = maxTokens,
                Temperature = (float)(cfg.OpenAI?.Temperature ?? 0.7f),
                TopP = (float)(cfg.OpenAI?.TopP ?? 1.0f)
            };

            var completion = await CallWithRetryAsync(
                () => chatClient.CompleteChatAsync(messages, options),
                _logger
            );

            var aiText = completion?.Content?.FirstOrDefault()?.Text
                ?? "No pude extraer información de la imagen.";

            // MÉTRICAS (chat con visión): prompt + respuesta
            await TrackChatUsageAsync(
                textPrompt ?? string.Empty,
                aiText,
                completion?.Usage?.InputTokenCount,
                completion?.Usage?.OutputTokenCount,
                CancellationToken.None);

            _logger.LogInformation("OpenAI Vision BYTES OK (tenant={Tenant})", _tenant.CurrentTenantId);
            return aiText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI Vision BYTES falló (tenant={Tenant})", _tenant.CurrentTenantId);
            return "⚠️ No pude analizar la imagen por ahora. Un agente te ayudará enseguida.";
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
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var res = await call();
                return res.Value; // ChatCompletion
            }
            catch (ClientResultException ex) when (ex.Status == 429 || ex.Status == 503)
            {
                lastException = ex;

                // Si es el último intento, no hacemos retry
                if (attempt == 3)
                {
                    logger.LogError(ex, "OpenAI {Status} tras 3 intentos. Fallando.", ex.Status);
                    throw; // Propaga la excepción original
                }

                // Leer Retry-After si existe
                string? retryAfterHeader = null;
                var rawResponse = ex.GetRawResponse();
                rawResponse?.Headers.TryGetValue("retry-after", out retryAfterHeader);

                var delay = int.TryParse(retryAfterHeader, out var seconds)
                    ? TimeSpan.FromSeconds(seconds)
                    : TimeSpan.FromMilliseconds(300 * attempt + Random.Shared.Next(0, 300));

                logger.LogWarning(
                    "OpenAI {Status} en intento {Attempt}/3. Reintentando en {Delay}ms...",
                    ex.Status, attempt, delay.TotalMilliseconds);

                await Task.Delay(delay, ct);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;

                if (attempt == 3)
                {
                    logger.LogError(ex, "Error de red tras 3 intentos. Fallando.");
                    throw;
                }

                var delay = TimeSpan.FromMilliseconds(300 * attempt + Random.Shared.Next(0, 300));
                logger.LogWarning(ex,
                    "Error de red en intento {Attempt}/3. Reintentando en {Delay}ms...",
                    attempt, delay.TotalMilliseconds);

                await Task.Delay(delay, ct);
            }
        }

        // Este código nunca debería ejecutarse, pero por seguridad:
        throw lastException ?? new InvalidOperationException("Retry loop terminó sin excepción");
    }

    /// <summary>
    /// Genera una respuesta completa a partir de una lista de mensajes (con roles), para historial dentro de la misma conversación.
    /// </summary>
    /// <param name="messages"></param>
    /// <param name="maxTokens"></param>
    /// <param name="model"></param>
    /// <returns></returns>
    public async Task<string> CompleteAsync(
     IEnumerable<ChatMessage> messages,
     int maxTokens = 400,
     string? model = null,
     CancellationToken ct = default)
    {
        var cfg = _tenant.Config;

        // Usa el modelo del tenant si no se especifica otro
        var selectedModel = model ?? cfg.OpenAI?.Model ?? "gpt-4o-mini";

        var client = EnsureClient();
        var chatClient = client.GetChatClient(selectedModel);

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = maxTokens,
            Temperature = (float)(cfg.OpenAI?.Temperature ?? 0.7f),
            TopP = (float)(cfg.OpenAI?.TopP ?? 1.0f)
        };

        _logger.LogDebug(
            "CompleteAsync → tenant={Tenant}, model={Model}, msgs={Count}, maxTokens={Max}",
            _tenant.CurrentTenantId, selectedModel, messages.Count(), maxTokens);

        var completion = await CallWithRetryAsync(
            () => chatClient.CompleteChatAsync(messages, options, cancellationToken: ct),
            _logger,
            ct
        );

        var assistantText = completion?.Content?.FirstOrDefault()?.Text ?? string.Empty;

        // Si el SDK no trae Usage, el helper estimará tokens por longitud.
        // Como aquí no tenemos un único "prompt", pasamos string.Empty;
        // si quieres, podemos reconstruir el texto del usuario más adelante.
        await TrackChatUsageAsync(
            string.Empty,
            assistantText,
            completion?.Usage?.InputTokenCount,
            completion?.Usage?.OutputTokenCount,
            ct);

        return assistantText;
    }

    /// <summary>
    /// Construye los mensajes de sistema (SystemPrompt + BusinessRules) desde la configuración del tenant.
    /// Método reutilizable en todos los métodos del servicio.
    /// </summary>
    private List<ChatMessage> BuildSystemMessages()
    {
        var cfg = _tenant.Config;
        var messages = new List<ChatMessage>();


        // ========= Estilo/tono por defecto (overridable) =========
        string tone = "profesional y cercano"; // p. ej.: "formal", "cálido y cercano", "juvenil"
        bool useEmoji = false;                 // true = permite 1 emoji cuando sea natural
        string language = "es";


        // (Opcional) lee estos ajustes desde BusinessRules si existen como propiedades simples
        try
        {
            if (cfg.BusinessRules is not null && cfg.BusinessRules.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = cfg.BusinessRules.RootElement;
                if (root.TryGetProperty("tone", out var t) && t.ValueKind == JsonValueKind.String)
                    tone = t.GetString()!;
                if (root.TryGetProperty("emoji", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    useEmoji = e.GetBoolean();
                if (root.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String)
                    language = l.GetString()!;
            }
        }
        catch { /* ignorar: si no hay estas propiedades, seguimos con defaults */ }

        // System prompt del tenant (con fallback seguro)
        var systemPrompt = string.IsNullOrWhiteSpace(cfg.OpenAI?.SystemPrompt)
            ? $@"
Eres el asistente virtual de {cfg.DisplayName}.
- Habla en {language} con un tono {tone}. {(useEmoji ? "Puedes usar como máximo 1 emoji cuando sea natural." : "No uses emojis.")}
- Sé claro, conciso, amable y experto en el negocio.

CUANDO HAYA CONTEXTO RAG:
- Si existe un mensaje del sistema que comience con 'CONTEXT (RAG):', usa EXCLUSIVAMENTE ese contexto para responder.
- Cita afirmaciones clave con [n] (n = índice del fragmento).
- Si el contexto no contiene la respuesta, dilo explícitamente y pide más detalles; NUNCA inventes.

REGLAS GENERALES:
- No inventes URLs, políticas, precios ni datos que no estén en el CONTEXTO o en las REGLAS DE NEGOCIO.
- Responde directo a la intención; usa listas breves (máximo 5 puntos) cuando ayuden.
- Si citaste fragmentos, cierra con una línea: 'Fuentes:' listando [n] Título de cada fragmento citado.
".Trim()
        : cfg.OpenAI!.SystemPrompt!.Trim();

        messages.Add(ChatMessage.CreateSystemMessage(systemPrompt));

        // Business rules del tenant (si existen)
        if (cfg.BusinessRules is not null)
        {
            try
            {
                var rulesText = cfg.BusinessRules.RootElement.GetRawText();
                if (!string.IsNullOrWhiteSpace(rulesText))
                {
                    messages.Add(ChatMessage.CreateSystemMessage(
                        $"REGLAS DE NEGOCIO (síguelas estrictamente ; nunca las contradigas):\n{rulesText}"
                    ));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "No se pudieron aplicar BusinessRules (tenant={Tenant})",
                    _tenant.CurrentTenantId);
            }
        }

        return messages;
    }

    /// <summary>
    /// Embed un solo texto, devolviendo el vector de floats.
    /// </summary>
    /// <param name="model"></param>
    /// <param name="text"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<float[]> GetEmbeddingAsync(string? model, string text, CancellationToken ct = default)
    {
        var vectors = await GetEmbeddingsAsync(model, [text], ct);
        return vectors.Length > 0 ? vectors[0] : Array.Empty<float>();
    }

    /// <summary>
    /// Embed múltiples textos, devolviendo un array de vectores de floats.
    /// </summary>
    /// <param name="model"></param>
    /// <param name="texts"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<float[][]> GetEmbeddingsAsync(string? model, IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts == null || texts.Count == 0)
            return Array.Empty<float[]>();

        var m = string.IsNullOrWhiteSpace(model) ? "text-embedding-3-small" : model;

        // Reusamos el proveedor de credenciales (igual que para chat)
        var apiKey = _keys.GetApiKey(_tenant.Config);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API key not found for tenant.");

        // Crea un cliente temporal de embeddings SDK 2.4.0 DE OPENAI
        var embClient = new EmbeddingClient(m, apiKey);

        // Fallback universal: 1 request por texto
        var tasks = texts.Select(t => embClient.GenerateEmbeddingAsync(input: t, cancellationToken: ct)).ToArray();
        var responses = await Task.WhenAll(tasks);

        // Delegamos la extracción del vector a un método aparte
        var result = new float[responses.Length][];
        for (int i = 0; i < responses.Length; i++)
        {
            result[i] = ExtractEmbeddingVector(responses[i].Value);
        }
        return result;
    }


    /// <summary>
    /// Método reflexivo para extraer el vector float[] de un objeto EmbeddingResponse o similar.
    /// Considera varias propiedades y métodos comunes en distintos SDKs.
    /// Para evitar dependencias directas, usa reflexión.
    /// </summary>
    /// <param name="embeddingObj"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    private static float[] ExtractEmbeddingVector(object embeddingObj)
    {
        if (embeddingObj is null) return [];

        var t = embeddingObj.GetType();

        // Propiedad directa común: Vector / Embedding / Values
        foreach (var name in new[] { "Vector", "Embedding", "Values" })
        {
            var p = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .FirstOrDefault(pp => string.Equals(pp.Name, name, StringComparison.OrdinalIgnoreCase));
            if (p != null)
            {
                var v = p.GetValue(embeddingObj);
                var arr = TryAsFloatArray(v);
                if (arr is not null) return arr;
            }
        }

        // Algunas variantes devuelven un contenedor: Data / Items / Embeddings
        foreach (var name in new[] { "Data", "Items", "Embeddings" })
        {
            var p = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .FirstOrDefault(pp => string.Equals(pp.Name, name, StringComparison.OrdinalIgnoreCase));
            if (p != null)
            {
                var listObj = p.GetValue(embeddingObj);
                if (listObj is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        var arr = ExtractEmbeddingVector(item!); // recursivo
                        if (arr.Length > 0) return arr;
                    }
                }
            }
        }

        // Método utilitario ocasional: ToArray/ToFloats/AsFloatArray
        foreach (var mname in new[] { "ToArray", "ToFloats", "AsFloatArray" })
        {
            var m = t.GetMethod(mname, BindingFlags.Public | BindingFlags.Instance, binder: null, types: Type.EmptyTypes, modifiers: null);
            if (m != null)
            {
                var v = m.Invoke(embeddingObj, null);
                var arr = TryAsFloatArray(v);
                if (arr is not null) return arr;
            }
        }

        //Si llegamos acá, no pudimos extraer el vector
        throw new InvalidOperationException($"No pude localizar el vector en tipo {t.FullName}. Revisa el SDK; si te dice el nombre de la propiedad, lo agrego a la lista.");
    }


    /// <summary>
    /// Método helper para intentar convertir un objeto a float[].
    /// </summary>
    /// <param name="v"></param>
    /// <returns></returns>
    private static float[]? TryAsFloatArray(object? v)
    {
        //Usa pattern matching para varios casos comunes
        switch (v)
        {
            //Casos comunes directos y convertibles a float[] 
            case null: return null; //Devuelve null si no hay valor
            case float[] fa: return fa; // ya es float[]
            case ReadOnlyMemory<float> rom: return rom.ToArray(); // ya es ReadOnlyMemory<float>
            case Memory<float> mem: return mem.ToArray(); // ya es Memory<float>
            case IEnumerable<float> ef: return ef.ToArray(); // ya es IEnumerable<float>
            case IEnumerable<double> ed: return ed.Select(x => (float)x).ToArray(); // es IEnumerable<double>, convertimos a float
            case System.Collections.IEnumerable ie:
                {
                    var list = new List<float>();
                    //Para cada elemento, intentamos convertir a float
                    foreach (var x in ie)
                    {
                        if (x is float f) list.Add(f);
                        else if (x is double d) list.Add((float)d);
                        else return null; // mezcla rara; probamos otro camino
                    }
                    return list.Count > 0 ? list.ToArray() : null;
                }
            default: return null;
        }
    }

    private async Task TrackChatUsageAsync(
        string promptText, 
        string answerText, 
        int? inputTokensFromSdk, 
        int? outputTokensFromSdk, 
        CancellationToken ct)
    {
        var inTok = inputTokensFromSdk ?? AiUtil.EstimateTokens(promptText);
        var outTok = outputTokensFromSdk ?? AiUtil.EstimateTokens(answerText);

        if (inTok <= 0 && outTok <= 0) return;

        var tenant = AiUtil.TenantSlug(_tenant);
        var provider = AiUtil.ProviderId(_tenant.Config);
        var model = AiUtil.ChatModelId(_tenant.Config);

        try
        {
            await _usage.TrackChatAsync(tenant, provider, model, inTok, outTok, estCostUsd: null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "No se pudo registrar métricas de chat (tenant={Tenant}, model={Model})",
                tenant, model);
        }
    }

    private OpenAIClient EnsureClient()
    {
        var slug = _tenant.Config?.Slug ?? _tenant.CurrentTenantId.Value;
        if (string.IsNullOrWhiteSpace(slug))
            throw new InvalidOperationException("Tenant no resuelto para esta petición (falta middleware o header X-Tenant-Slug).");

        var apiKey = _keys.GetApiKey(_tenant.Config);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"OpenAI.ApiKey no configurada para tenant '{slug}'.");
        if (_client is null || !string.Equals(apiKey, _cachedApiKey, StringComparison.Ordinal))
        {
            lock (_clientLock)
            {
                // Re-check dentro del lock
                if (_client is null || !string.Equals(apiKey, _cachedApiKey, StringComparison.Ordinal))
                {
                    _cachedApiKey = apiKey;
                    _client = new OpenAIClient(apiKey);
                    _logger.LogInformation(
                        "OpenAiService: cliente (re)creado para tenant={Tenant}", slug);
                }
            }
        }
        return _client;
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
            var client = EnsureClient();

            var model = _tenant.Config?.OpenAI?.Model ?? "gpt-4o-mini";

            // ✅ usa siempre chatClient (coherente con el resto del archivo)
            var chatClient = client.GetChatClient(model);

            var resp = await chatClient.CompleteChatAsync(
                [ChatMessage.CreateUserMessage("ping")],
                new ChatCompletionOptions { MaxOutputTokenCount = 1 }
            );

            var comp = resp.Value;

            return comp?.Content is not null && comp.Content.Any();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ping OpenAI falló (tenant={Tenant})", _tenant.CurrentTenantId.Value);
            return false;
        }
    }

}