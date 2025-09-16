using KommoAIAgent.Models;

namespace KommoAIAgent.Services.Interfaces
{
    /// <summary>
    /// Contrato para el servicio que orquesta toda la lógica de un webhook entrante.
    /// Su única responsabilidad es recibir el payload y asegurarse de que se procese correctamente.
    /// </summary>
    public interface IWebhookHandler
    {
        /// <summary>
        /// Procesa el payload deserializado del webhook de Kommo.
        /// </summary>
        /// <param name="payload">El objeto que contiene los datos del mensaje entrante.</param>
        /// <returns>Una tarea que se completa cuando el procesamiento ha terminado.</returns>
        Task ProcessIncomingMessageAsync(KommoWebhookPayload payload);
    }
}
