namespace KommoAIAgent.Application.Connectors;

/// <summary>
/// Intent detectado del usuario (puede requerir un conector externo)
/// </summary>
public sealed record ExternalIntent
{
    /// <summary>
    /// Capability a invocar (ej: 'cancel_appointment')
    /// </summary>
    public string? Capability { get; init; }

    /// <summary>
    /// Tipo de conector requerido (ej: 'isalud_api')
    /// </summary>
    public string? ConnectorType { get; init; }

    /// <summary>
    /// Indica si requiere invocar un conector externo
    /// </summary>
    public bool RequiresConnector { get; init; }

    /// <summary>
    /// Parámetros extraídos del mensaje del usuario
    /// </summary>
    public Dictionary<string, object> Parameters { get; init; } = new();

    /// <summary>
    /// Confidence score (0.0 - 1.0)
    /// </summary>
    public float Confidence { get; init; }

    /// <summary>
    /// Razón de la clasificación (para debugging)
    /// </summary>
    public string? Reasoning { get; init; }
}

/// <summary>
/// Detector de intenciones basado en LLM
/// </summary>
public interface IIntentDetector
{
    /// <summary>
    /// Analiza el mensaje del usuario y determina si requiere un conector externo
    /// </summary>
    Task<ExternalIntent> DetectAsync(
        string userMessage,
        string tenantSlug,
        CancellationToken ct = default
    );
}