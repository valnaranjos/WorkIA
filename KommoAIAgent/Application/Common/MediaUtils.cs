namespace KommoAIAgent.Application.Common
{
    /// <summary>
    /// Helpers comunes para media (URLs, attachments, etc).
    /// </summary>
    public class MediaUtils
    {
        /// <summary>
        /// Devuelve la URL sin query ni fragmentos (oculta tokens/ids sensibles).
        /// Si no es una URL absoluta válida, devuelve el valor original.
        /// </summary>
        public static string MaskUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url!;
            return uri.GetLeftPart(UriPartial.Path);
        }

        /// <summary>
        /// Si el attachment es una imagen (por mime, tipo o extensión), devuelve true.
        /// </summary>
        /// <param name="a"></param>
        /// <returns></returns>
        public static bool IsImageAttachment(AttachmentInfo a)
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
        /// Si el parametro es nulo o vacío, devuelve el siguiente no vacío, o null si todos lo son.
        /// Usado para mensajes de fallback.
        /// </summary>
        /// <param name="values"></param>
        /// <returns></returns>

        public static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return null;
        }

    }
}
