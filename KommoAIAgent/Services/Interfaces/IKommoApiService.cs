using KommoAIAgent.Models;

namespace KommoAIAgent.Services.Interfaces
{
    /// <summary>
    /// Contrato para el servicio que se comunica con la API de Kommo.
    /// Abstrae todas las llamadas a la API de Kommo en métodos claros y específicos.
    /// </summary>
    public interface IKommoApiService
    {
        /// <summary>
        /// Actualiza el valor de un campo de texto personalizado para un lead específico.
        /// </summary>
        /// <param name="leadId">El ID del lead que se va to modificar.</param>
        /// <param name="fieldId">El ID del campo personalizado a actualizar.</param>
        /// <param name="value">El nuevo valor de texto que se escribirá en el campo.</param>
        /// <returns>Una tarea que se completa cuando la actualización ha finalizado.</returns>
        Task UpdateLeadFieldAsync(long leadId, long fieldId, string value);

        /// <summary>
        /// (Opcional, para futuro) Obtiene la información de contexto de un lead.
        /// </summary>
        /// <param name="leadId">El ID del lead a consultar.</param>
        /// <returns>Un objeto KommoLead con la información del lead.</returns>
        Task<KommoLead?> GetLeadContextByIdAsync(long leadId);


        /// <summary>
        /// Descarga un archivo adjunto desde una URL pública, valida el tamaño y el tipo MIME.
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        Task<(byte[] bytes, string mime, string? fileName)> DownloadAttachmentAsync(string url);


        /// <summary>
        /// Actualiza directamente el campo MensajeIA de un lead (según config del tenant).
        /// </summary>
        /// <param name="leadId">El ID del lead a actualizar.</param>
        /// <param name="texto">El valor de texto a poner en MensajeIA.</param>
        /// <param name="ct">Token de cancelación opcional.</param>
        Task UpdateLeadMensajeIAAsync(long leadId, string texto, CancellationToken ct = default);

    }
}
