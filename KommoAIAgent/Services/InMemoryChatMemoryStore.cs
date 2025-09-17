using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using KommoAIAgent.Services.Interfaces;

namespace KommoAIAgent.Services
{
    /// <summary>
    /// Impleme´ntación en memoria de IChatMemoryStore con límite de mensajes guardados por turno.
    /// </summary>
    public class InMemoryChatMemoryStore : IChatMemoryStore
    {
        //Incluye timestamp, rol y contenido.
        private readonly ConcurrentDictionary<long, LinkedList<(DateTimeOffset Ts, string Role, string Content)>> _mem = new();
        private const int MaxKeep = 20;

        private readonly TimeSpan _ttl;

        //Trae la configuración de tiempo de vida desde configuración, por defecto 12 horas.
        public InMemoryChatMemoryStore(IConfiguration cfg)
        {
            var hours = int.TryParse(cfg["Memory:TTLHours"], out var h) ? h : 12;
            _ttl = TimeSpan.FromHours(hours);
        }


        /// <summary>
        /// Obtiene la conversación previa para un lead, hasta un máximo de turnos.
        /// </summary>
        /// <param name="leadId"></param>
        /// <param name="maxTurns"></param>
        /// <returns></returns>
        public IReadOnlyList<(string Role, string Content)> Get(long leadId, int maxTurns = 12)
        {
            if (!_mem.TryGetValue(leadId, out var list))
            return [];

            var now = DateTimeOffset.UtcNow;

            // Purga por TTL (desde el frente de la lista, que es lo más viejo)
            while (list.First is not null && (now - list.First!.Value.Ts) > _ttl)
                list.RemoveFirst();

            // Devuelve los últimos maxTurns, en orden cronológico
            return [.. list
                .Reverse()                 // de más nuevo a más viejo
                .Take(maxTurns)
                .Select(x => (x.Role, x.Content))
                .Reverse()];
        }


        /// Trae el turno del usuario o del asistente.
        public void AppendUser(long leadId, string content) => Append(leadId, ("user", content));
        public void AppendAssistant(long leadId, string content) => Append(leadId, ("assistant", content));


        /// <summary>
        /// Limpia la conversación para un lead específico.
        /// </summary>
        /// <param name="leadId"></param>
        public void Clear(long leadId) => _mem.TryRemove(leadId, out _);


        /// <summary>
        /// Agrega un turno a la conversación del lead.
        /// </summary>
        /// <param name="leadId"></param>
        /// <param name="turn"></param>
        private void Append(long leadId, (string Role, string Content) turn)
        {
            var list = _mem.GetOrAdd(leadId, _ => new LinkedList<(DateTimeOffset, string, string)>());
            list.AddLast((DateTimeOffset.UtcNow, turn.Role, turn.Content));

            // Límite duro de elementos guardados (independiente del TTL)
            while (list.Count > MaxKeep) list.RemoveFirst();
        }     
    }
}
