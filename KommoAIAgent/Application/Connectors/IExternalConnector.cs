namespace KommoAIAgent.Application.Connectors;

/// <summary>
/// Contrato para cualquier conector externo (iSalud, SAP, Dynamics, etc.)
/// </summary>
public interface IExternalConnector
{
    /// <summary>
    /// Tipo único del conector (ej: 'isalud_api', 'dynamics365_crm')
    /// </summary>
    string ConnectorType { get; }

    /// <summary>
    /// Nombre legible del conector
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Lista de capacidades (capabilities) que soporta este conector
    /// </summary>
    IReadOnlyList<string> Capabilities { get; }

    /// <summary>
    /// Invoca una acción específica en el sistema externo
    /// </summary>
    /// <param name="capability">Nombre de la acción (ej: 'cancel_appointment')</param>
    /// <param name="parameters">Parámetros de la acción</param>
    /// <param name="ct">Token de cancelación</param>
    /// <returns>Respuesta del conector</returns>
    Task<ConnectorResponse> InvokeAsync(
        string capability,
        Dictionary<string, object> parameters,
        CancellationToken ct = default
    );

    /// <summary>
    /// Verifica conectividad con el sistema externo (health check)
    /// </summary>
    Task<bool> HealthCheckAsync(CancellationToken ct = default);
}

/// <summary>
/// Respuesta estandarizada de cualquier conector
/// </summary>
public sealed record ConnectorResponse
{
    /// <summary>
    /// Indica si la operación fue exitosa
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Mensaje legible para el usuario final (puede mostrarse directamente en Kommo)
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Datos estructurados de la respuesta (opcional)
    /// </summary>
    public object? Data { get; init; }

    /// <summary>
    /// Metadatos adicionales (HTTP status, latency, etc.)
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Mensaje de error técnico (solo si Success = false)
    /// </summary>
    public string? ErrorDetails { get; init; }

    /// <summary>
    /// Código de error del sistema externo (opcional)
    /// </summary>
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Request estandarizado para invocar conectores (usado por microservicios)
/// </summary>
public sealed record ConnectorRequest
{
    public required string Capability { get; init; }
    public required Dictionary<string, object> Parameters { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}