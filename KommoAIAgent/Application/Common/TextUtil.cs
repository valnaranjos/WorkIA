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
    }
}
