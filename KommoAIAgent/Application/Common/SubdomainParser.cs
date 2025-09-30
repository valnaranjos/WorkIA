namespace KommoAIAgent.Application.Common
{
    /// <summary>
    /// Helper para derivar el subdominio (slug) a partir de la URL base de Kommo.
    /// </summary>
    public static class SubdomainParser
    {
        /// <summary>
        /// Deriva el subdominio (slug) a partir de la URL base de Kommo.
        /// </summary>
        /// <param name="baseUrl"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static string DeriveSlug(string baseUrl)
        {
            var uri = new Uri(baseUrl);
            var parts = uri.Host.Split('.');
            var slug = parts.Length >= 3 ? parts[0].ToLowerInvariant() : throw new ArgumentException("No se pudo derivar subdominio de KommoBaseUrl");
            return slug.Length <= 100 ? slug : slug[..100];
        }
    }
}
