using System.Text;

namespace KommoAIAgent.Application.Common
{
    /// <summary>
    /// Helper para operaciones comunes con texto.
    /// </summary>
    public class TextUtil
    {

        /// <summary>
        ///  Revisa si una cadena es nula o vacía, y la trunca a una longitud máxima si es necesario.
        /// </summary>
        /// <param name="s"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public static string Truncate(string s, int max) =>
           string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max];


        /// <summary>
        /// Normaliza una cadena para comparaciones: trim y minúsculas.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static string Normalize(string? s) =>
              (s ?? "").Trim().ToLowerInvariant();
    
     /// <summary>
    /// Canonicaliza texto para la clave del caché de embeddings:
    /// - Unicode NFKC (para unificar caracteres equivalentes)
    /// - Unifica saltos de línea
    /// - Trim
    /// - Colapsa espacios consecutivos en uno
    /// - NO altera mayúsculas/minúsculas
    /// </summary>
    public static string NormalizeForEmbeddingKey(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            // Unicode (combining marks, etc.)
            var t = s.Normalize(NormalizationForm.FormKC);

            // Unifica saltos de línea y recorta
            t = t.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

            //Colapsa runs de whitespace a un solo espacio
            var sb = new StringBuilder(t.Length);
            bool prevSpace = false;
            foreach (var ch in t)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!prevSpace) sb.Append(' ');
                    prevSpace = true;
                }
                else
                {
                    sb.Append(ch);
                    prevSpace = false;
                }
            }
            return sb.ToString();
        }
    }
}
