using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Services.Interfaces;
using OpenAI.Chat;

namespace KommoAIAgent.Helpers
{
    /// <summary>
    /// Composición centralizada del prompt/historial para chat (texto e imágenes).
    /// </summary>
    public static class ChatComposer
    {

        /// <summary>
        /// Construye la lista de mensajes para OpenAI:
        ///  1) Mensaje de sistema (instrucciones)
        ///  2) Historial (últimos N turnos) leído desde IChatMemoryStore (multi-tenant)
        /// </summary>
        /// <param name="store">Memoria conversacional (multi-tenant)</param>
        /// <param name="tenant">Contexto del tenant actual</param>
        /// <param name="leadId">Lead de Kommo</param>
        /// <param name="systemPrompt">Instrucciones del sistema</param>
        /// <param name="historyTurns">Cuántos turnos previos recuperar</param>
        /// <param name="ct">CancellationToken del request (opcional)</param>
        public static async Task<List<ChatMessage>> BuildHistoryMessagesAsync(
            IChatMemoryStore store,
            ITenantContext tenant,
            long leadId,
            string systemPrompt,
            int historyTurns = 10,
            CancellationToken ct = default)
        {
            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage(systemPrompt)
            };


            // Lee historial usando el contrato NUEVO (async + tenant)
            var history = await store.GetAsync(
                tenant.CurrentTenantId.Value, // slug/Id del tenant
                leadId,
                historyTurns,
                ct
            );

            // Pone el historial en el formato del SDK
            foreach (var (role, content) in history)
            {
                messages.Add(
                    role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                        ? ChatMessage.CreateAssistantMessage(content)
                        : ChatMessage.CreateUserMessage(content)
                );
            }

            return messages;
        }


        /// <summary>
        /// Agg un mensaje de usuario que puede incluir texto.
        /// </summary>
        /// <param name="messages"></param>
        /// <param name="text"></param>
        public static void AppendUserText(List<ChatMessage> messages, string text)
        {
            messages.Add(ChatMessage.CreateUserMessage(text));
        }

        /// <summary>
        /// Obtiene un mensaje de usuario que incluye texto e imagen.
        /// </summary>
        /// <param name="messages"></param>
        /// <param name="text"></param>
        /// <param name="imageBytes"></param>
        /// <param name="mimeType"></param>

        public static void AppendUserTextAndImage(
            List<ChatMessage> messages, string text, byte[] imageBytes, string mimeType)
        {
            messages.Add(
                ChatMessage.CreateUserMessage(
                    ChatMessageContentPart.CreateTextPart(text),
                    ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), mimeType)
                )
            );
        }
    }
}
