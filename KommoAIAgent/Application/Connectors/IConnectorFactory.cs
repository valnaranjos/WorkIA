namespace KommoAIAgent.Application.Connectors;

/// <summary>
/// Factory para crear instancias de conectores externos por tenant
/// </summary>
public interface IConnectorFactory
{
    /// <summary>
    /// Obtiene un conector específico por tipo para el tenant actual
    /// </summary>
    /// <param name="tenantSlug">Slug del tenant</param>
    /// <param name="connectorType">Tipo de conector (ej: 'isalud_api')</param>
    /// <param name="ct">Token de cancelación</param>
    /// <returns>Conector o null si no existe/está inactivo</returns>
    Task<IExternalConnector?> GetConnectorAsync(
        string tenantSlug,
        string connectorType,
        CancellationToken ct = default
    );

    /// <summary>
    /// Obtiene todos los conectores activos de un tenant
    /// </summary>
    Task<IReadOnlyList<IExternalConnector>> GetAllConnectorsAsync(
        string tenantSlug,
        CancellationToken ct = default
    );

    /// <summary>
    /// Busca un conector que tenga una capability específica
    /// </summary>
    Task<IExternalConnector?> FindConnectorByCapabilityAsync(
        string tenantSlug,
        string capability,
        CancellationToken ct = default
    );
}