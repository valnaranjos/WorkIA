using KommoAIAgent.Application.Tenancy;
using System.Collections.Generic;

namespace KommoAIAgent.Services.Interfaces
{
        /// <summary>
        /// Interfaz para almacenar y recuperar conversaciones.
        /// </summary>
        public interface IChatMemoryStore
    {         
        /// <summary>
        /// Agrega un turno (user/assitant) con TLL
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="leadId"></param>
        /// <param name="role"></param>
        /// <param name="content"></param>
        /// <param name="ttl"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task AppendAsync(string tenant, long leadId, string role, string content, TimeSpan ttl, CancellationToken ct = default);

     
        /// <summary>
        /// Lee los últimos N turnos (en orden cronólógico)
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="leadId"></param>
        /// <param name="lastN"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<IReadOnlyList<(string role, string content)>> GetAsync(string tenant, long leadId, int lastN, CancellationToken ct = default);


        /// <summary>
        /// Limpia el historial de un lead.
        /// </summary>
        /// <param name="tenant"></param>
        /// <param name="leadId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task ClearAsync(string tenant, long leadId, CancellationToken ct = default);
    }

    /// <summary>
    /// Helpers de azúcar para roles comunes.
    /// </summary>
    public static class ChatMemoryStoreExtensions
    {
        public static Task AppendUserAsync(
         this IChatMemoryStore store,
         ITenantContext tenant,
         long leadId,
         string content,
         CancellationToken ct = default)
        {
            var ttl = TimeSpan.FromMinutes(tenant.Config.Memory?.TTLMinutes ?? 120);
            return store.AppendAsync(tenant.CurrentTenantId.Value, leadId, "user", content, ttl, ct);
        }

        public static Task AppendAssistantAsync(
            this IChatMemoryStore store,
            ITenantContext tenant,
            long leadId,
            string content,
            CancellationToken ct = default)
        {
            var ttl = TimeSpan.FromMinutes(tenant.Config.Memory?.TTLMinutes ?? 120);
            return store.AppendAsync(tenant.CurrentTenantId.Value, leadId, "assistant", content, ttl, ct);
        }
    }
}
