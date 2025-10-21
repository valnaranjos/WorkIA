using KommoAIAgent.Application.Connectors;
using KommoAIAgent.Application.Interfaces;
using System.Text.Json;

namespace KommoAIAgent.Infrastructure.Connectors;

/// <summary>
/// Detector de intenciones usando LLM (OpenAI)
/// Determina si el mensaje del usuario requiere invocar un conector externo
/// </summary>
public sealed class LlmIntentDetector : IIntentDetector
{
    private readonly IConnectorFactory _connectorFactory;
    private readonly IAiService _aiService;
    private readonly ILogger<LlmIntentDetector> _logger;

    public LlmIntentDetector(
        IConnectorFactory connectorFactory,
        IAiService aiService,
        ILogger<LlmIntentDetector> logger)
    {
        _connectorFactory = connectorFactory;
        _aiService = aiService;
        _logger = logger;
    }

    public async Task<ExternalIntent> DetectAsync(
     string userMessage,
     string tenantSlug,
     CancellationToken ct = default)
    {
        // Obtener conectores disponibles para este tenant
        var connectors = await _connectorFactory.GetAllConnectorsAsync(tenantSlug, ct);

        if (connectors.Count == 0)
        {
            _logger.LogDebug("No connectors configured for tenant {Tenant}", tenantSlug);
            return new ExternalIntent
            {
                RequiresConnector = false,
                Confidence = 1.0f,
                Reasoning = "No hay conectores configurados"
            };
        }

        // Construir lista de capabilities disponibles
        var capabilitiesMap = new Dictionary<string, string>(); // capability -> connectorType
        foreach (var connector in connectors)
        {
            foreach (var cap in connector.Capabilities)
            {
                capabilitiesMap[cap] = connector.ConnectorType;
            }
        }

        if (capabilitiesMap.Count == 0)
        {
            return new ExternalIntent
            {
                RequiresConnector = false,
                Confidence = 1.0f,
                Reasoning = "Conectores sin capabilities"
            };
        }

        // Construir prompt de clasificación
        var capabilitiesList = string.Join("\n",
            capabilitiesMap.Select(kv => $"  - {kv.Key} (conector: {kv.Value})")
        );

        var prompt = $@"Eres un clasificador de intenciones. Analiza el siguiente mensaje del usuario y determina si requiere invocar un sistema externo.

**Capabilities disponibles:**
{capabilitiesList}

**Mensaje del usuario:**
""{userMessage}""

**Instrucciones:**
1. Si el mensaje NO requiere un sistema externo (es solo conversación, pregunta general, etc.), responde:
   {{""requiresConnector"": false, ""confidence"": 0.95}}

2. Si el mensaje SÍ requiere un sistema externo:
   - Identifica la capability más adecuada
   - Extrae parámetros relevantes del mensaje (fechas, IDs, nombres, etc.)
   - Responde en este formato EXACTO (JSON válido):

{{
  ""requiresConnector"": true,
  ""capability"": ""nombre_capability"",
  ""connectorType"": ""tipo_conector"",
  ""parameters"": {{
    ""param1"": ""valor1"",
    ""param2"": ""valor2""
  }},
  ""confidence"": 0.85,
  ""reasoning"": ""El usuario quiere...""
}}

**IMPORTANTE:**
- Responde SOLO el JSON, sin texto adicional
- Si hay fechas, normalízalas a formato ISO 8601 (YYYY-MM-DD)
- Si hay IDs/documentos, extráelos como string
- Confidence entre 0.0 y 1.0

Respuesta JSON:";

        string aiResponse = string.Empty; // 🔧 FIX: Declarar FUERA del try

        try
        {
            // Llamar al LLM
            aiResponse = await _aiService.GenerateContextualResponseAsync(prompt, ct);

            _logger.LogDebug("LLM response for intent detection: {Response}", aiResponse);

            // Limpiar respuesta (por si el LLM agrega texto extra)
            var jsonStart = aiResponse.IndexOf('{');
            var jsonEnd = aiResponse.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = aiResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var intentJson = JsonDocument.Parse(jsonStr);
                var root = intentJson.RootElement;

                var requiresConnector = root.GetProperty("requiresConnector").GetBoolean();

                if (!requiresConnector)
                {
                    return new ExternalIntent
                    {
                        RequiresConnector = false,
                        Confidence = root.TryGetProperty("confidence", out var conf)
                            ? conf.GetSingle()
                            : 0.95f
                    };
                }

                // Parsear intent completo
                var capability = root.GetProperty("capability").GetString()!;
                var connectorType = root.GetProperty("connectorType").GetString()!;
                var confidence = root.TryGetProperty("confidence", out var confProp)
                    ? confProp.GetSingle()
                    : 0.8f;
                var reasoning = root.TryGetProperty("reasoning", out var reasonProp)
                    ? reasonProp.GetString()
                    : null;

                var parameters = new Dictionary<string, object>();
                if (root.TryGetProperty("parameters", out var paramsObj))
                {
                    foreach (var prop in paramsObj.EnumerateObject())
                    {
                        parameters[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString()!,
                            JsonValueKind.Number => prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.Value.GetRawText()
                        };
                    }
                }

                _logger.LogInformation(
                    "Intent detected: capability={Capability}, connector={Connector}, confidence={Confidence}",
                    capability, connectorType, confidence
                );

                return new ExternalIntent
                {
                    RequiresConnector = true,
                    Capability = capability,
                    ConnectorType = connectorType,
                    Parameters = parameters,
                    Confidence = confidence,
                    Reasoning = reasoning
                };
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response as JSON: {Response}", aiResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting intent");
        }

        // Fallback: no se pudo detectar intent
        return new ExternalIntent
        {
            RequiresConnector = false,
            Confidence = 0.0f,
            Reasoning = "Error al analizar intent"
        };
    }
}