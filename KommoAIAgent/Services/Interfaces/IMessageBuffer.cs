using KommoAIAgent.Models;

namespace KommoAIAgent.Services.Interfaces
{
     public record AggregatedMessage(string Text, List<AttachmentInfo> Attachments);

    public interface IMessageBuffer
    {
        /// <summary>
        /// Ofrece un mensaje al buffer del lead. Programa el envío cuando:
        /// - Pase la ventana (WindowMs) sin nuevos mensajes, o
        /// - Se alcance MaxBurstMs, o
        /// - Llegue una imagen (disparo inmediato).
        /// </summary>
        Task OfferAsync(
            long leadId,
            string? text,
            IReadOnlyList<AttachmentInfo> attachments,
            Func<long, AggregatedMessage, Task> onFlush,
            CancellationToken ct = default);

        /// <summary>
        /// Borra el estado del buffer para un lead específico.
        /// </summary>
        /// <param name="leadId"></param>
        void Clear(long leadId);
    }
}
