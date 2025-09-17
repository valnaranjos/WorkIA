using KommoAIAgent.Services.Interfaces;
using OpenAI.Chat;

namespace KommoAIAgent.Helpers
{
    /// <summary>
    /// Provee métodos para componer mensajes de chat con historial y contenido mixto (texto e imágenes).
    /// De forma centralizada y reutilizable.
    /// </summary>
    public class ChatComposer
    {

        /// <summary>
        /// Construye una lista de mensajes de chat que incluye un mensaje del sistema y el historial de la conversación.
        /// </summary>
        /// <param name="conv"></param>
        /// <param name="leadId"></param>
        /// <param name="systemPrompt"></param>
        /// <param name="historyTurns"></param>
        /// <returns></returns>
        public static List<ChatMessage> BuildHistoryMessages(
            IChatMemoryStore conv, long leadId, string systemPrompt, int historyTurns = 10)
        {
            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage(systemPrompt)
            };

            var history = conv.Get(leadId, historyTurns);
            foreach (var turn in history)
            {
                messages.Add(turn.Role == "assistant"
                    ? ChatMessage.CreateAssistantMessage(turn.Content)
                    : ChatMessage.CreateUserMessage(turn.Content));
            }
            return messages;
        }


        /// <summary>
        /// Obtiene un mensaje de usuario que puede incluir texto.
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
