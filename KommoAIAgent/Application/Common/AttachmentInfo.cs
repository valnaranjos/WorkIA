namespace KommoAIAgent.Application.Common
{
    /// <summary>
    /// Modelo de datos para representar un archivo adjunto en un mensaje.
    /// </summary>
    public class AttachmentInfo
    {
        public string? Type { get; set; } // "image", "file", etc.
        public string? Url { get; set; }  // La URL para descargar el archivo.
        public string? Name { get; set; }

        public string? MimeType { get; set; } // Tipo MIME del archivo, si está disponible.
    }
}
