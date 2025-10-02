using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Domain.Tenancy;
using Microsoft.Extensions.Primitives;
using System.Text.RegularExpressions;

namespace KommoAIAgent.Infrastructure.Tenancy
{
    /// <summary>
    /// Resuelve el tenant actual a partir de la petición HTTP, de varias formas: ruta, subdominio, header o payload de webhook.
    /// </summary>
    public sealed class TenantResolver : ITenantResolver        
    {
        // Regex para extraer el slug de la ruta /t/{slug}
        private static readonly Regex RouteSlug = new(@"^/t/(?<slug>[a-z0-9\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Aquí se implementa la lógica de resolución del tenant actual
        /// Permite resolver desde varias fuentes, para el webhook de Kommo lo hace a través de la configuración del webhook acompañado de ?tenant="cuenta"
        /// </summary>
        /// <param name="http"></param>
        /// <returns></returns>

        public TenantId Resolve(HttpContext http)
        {
            static bool IsValid(string? s)
            => !string.IsNullOrWhiteSpace(s) && s != "?" && s != "%3F";

            // 1) Header (Swagger/Kommo si lo configuras)
            if (http.Request.Headers.TryGetValue("X-Tenant-Slug", out var h))
            {
                var v = h.ToString();
                if (IsValid(v)) return TenantId.From(v);
            }

            // 2) Querystring ?tenant=slug  (recomendado para Kommo)
            if (http.Request.Query.TryGetValue("tenant", out var q))
            {
                var v = q.ToString();
                if (IsValid(v)) return TenantId.From(v);
            }

            // 3) Ruta /t/{slug}
            var path = http.Request.Path.Value ?? string.Empty;
            var m = RouteSlug.Match(path);
            if (m.Success && IsValid(m.Groups["slug"].Value))
                return TenantId.From(m.Groups["slug"].Value);

            // 4) Subdominio (evitar ngrok/localhost)
            var host = http.Request.Host.Host ?? "";
            if (!host.Contains("ngrok", StringComparison.OrdinalIgnoreCase)
                && !host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
                && !host.StartsWith("127.", StringComparison.OrdinalIgnoreCase))
            {
                var sub = host.Split('.').FirstOrDefault();
                if (IsValid(sub)) return TenantId.From(sub!);
            }

            // 5) Nada válido
            return TenantId.From(string.Empty);
        }       
    }
}
