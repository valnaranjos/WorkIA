using KommoAIAgent.Models;

namespace KommoAIAgent.Helpers
{
    public class Utils
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
    }
}
