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
    }
}
