namespace KommoAIAgent.Infrastructure
{
    using KommoAIAgent.Application.Common;

    /// <summary>
    /// Helper para operaciones comunes con attachments.
    /// </summary>
    public static class AttachmentHelper
    {
        /// <summary>
        /// Método que intenta determinar si un attachment es una imagen. (para posteriores versiones con adjuntos o .zips)
        /// </summary>
        /// <param name="a"></param>
        /// <returns></returns>
        public static bool IsImage(AttachmentInfo a)
        {
            if (!string.IsNullOrWhiteSpace(a.MimeType) &&
                a.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(a.Type) &&
                a.Type.Contains("image", StringComparison.OrdinalIgnoreCase))
                return true;

            var s = (a.Url ?? a.Name ?? "").ToLowerInvariant();
            return s.EndsWith(".jpg") || s.EndsWith(".jpeg") || s.EndsWith(".png") ||
                   s.EndsWith(".webp") || s.EndsWith(".gif") || s.EndsWith(".bmp") ||
                   s.EndsWith(".tif") || s.EndsWith(".tiff");
        }


        /// <summary>
        /// Obtiene un MIME a partir  de la URL o el nombre del archivo que da Kommo.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="name"></param>
        /// Permite imagenes tipo png, jpg, jpeg, webp, gif, bmp, tif, tiff
        /// <returns></returns>
        public static string GuessMimeFromUrlOrName(string url, string? name)
        {
            var s = (name ?? url).ToLowerInvariant();
            if (s.EndsWith(".png")) return "image/png";
            if (s.EndsWith(".jpg") || s.EndsWith(".jpeg")) return "image/jpeg";
            if (s.EndsWith(".webp")) return "image/webp";
            if (s.EndsWith(".gif")) return "image/gif";
            if (s.EndsWith(".bmp")) return "image/bmp";
            if (s.EndsWith(".tif") || s.EndsWith(".tiff")) return "image/tiff";
            return "image/jpeg";
        }
    }
}