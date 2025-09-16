using System.Collections.Generic;

namespace KommoAIAgent.Services.Interfaces
{
        /// <summary>
        /// Interfaz para almacenar y recuperar conversaciones.
        /// </summary>
        public interface IChatMemoryStore
        {
            /// <summary>
            /// Inicializa una nueva conversación para un lead.
            /// </summary>
            /// <param name="leadId"></param>
            /// <param name="maxTurns"></param>
            /// <returns></returns>
            IReadOnlyList<(string Role, string Content)> Get(long leadId, int maxTurns = 12);

            /// <summary>
            /// Agrega un mensaje del usuario a la conversación.
            /// </summary>
            /// <param name="leadId"></param>
            /// <param name="content"></param>
            void AppendUser(long leadId, string content);

            /// <summary>
            /// Agrega un mensaje del asistente a la conversación.
            /// </summary>
            /// <param name="leadId"></param>
            /// <param name="content"></param>
            void AppendAssistant(long leadId, string content);

            /// <summary>
            /// Limpia la conversación para un lead específico.
            /// </summary>
            /// <param name="leadId"></param>
            void Clear(long leadId);
        }
}
