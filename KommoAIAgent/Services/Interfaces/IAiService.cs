using OpenAI.Chat;

namespace KommoAIAgent.Services.Interfaces
{
    /// <summary>
    /// Contrato para cualquier servicio de Inteligencia Artificial.
    /// Define las capacidades de generación de texto que necesita nuestra aplicación.
    /// </summary>
    public interface IAiService
    {
        /// <summary>
        /// Genera una respuesta de texto contextual a partir de un mensaje de entrada.
        /// </summary>
        /// <param name="textPrompt">El mensaje del usuario que la IA debe procesar.</param>
        /// <returns>Una cadena de texto con la respuesta generada por la IA.</returns>
        Task<string> GenerateContextualResponseAsync(string textPrompt);

        /// <summary>
        /// Analiza una imagen y genera una respuesta contextual, usando un texto de apoyo.
        /// </summary>
        /// <param name="textPrompt">El texto que acompaña a la imagen (puede estar vacío).</param>
        /// <param name="imageUrl">La URL pública de la imagen a analizar.</param>
        /// <returns>Una cadena de texto con el análisis o la respuesta de la IA sobre la imagen.</returns>
        Task<string> AnalyzeImageAndRespondAsync(string textPrompt, string imageUrl);

        /// <summary>
        /// Contacto para analizar una imagen a partir de un arreglo de bytes.
        /// </summary>
        /// <param name="textPrompt"></param>
        /// <param name="imageBytes"></param>
        /// <param name="mimeType"></param>
        /// <returns></returns>
        Task<string> AnalyzeImageFromBytesAsync(string textPrompt, byte[] imageBytes, string mimeType);

        /// <summary>
        /// Contrato para verificar la conectividad con el servicio de IA, diferencia si se cae IA o Kommo.
        /// </summary>
        /// <returns></returns>
        Task<bool> PingAsync();

        /// <summary>
        /// Recibe una lista de mensajes (con roles) y genera una respuesta completa.
        /// </summary>
        /// <param name="messages"></param>
        /// <param name="maxTokens"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        Task<string> CompleteAsync(IEnumerable<ChatMessage> messages, int maxTokens = 400, string? model = null);

    }
}