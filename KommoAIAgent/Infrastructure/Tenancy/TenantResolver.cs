using KommoAIAgent.Application.Tenancy;
using KommoAIAgent.Domain.Tenancy;
using Microsoft.Extensions.Primitives;
using System.Text.RegularExpressions;

namespace KommoAIAgent.Infraestructure.Tenancy
{
    /// <summary>
    /// Resuelve el tenant actual a partir de la petición HTTP, de varias formas: ruta, subdominio, header o payload de webhook.
    /// </summary>
    public sealed class TenantResolver : ITenantResolver        
    {
        // Regex para extraer el slug de la ruta /t/{slug}
        private static readonly Regex RouteSlug = new(@"^/t/(?<slug>[a-z0-9\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        
        public TenantId Resolve(HttpContext http)
        {
            //Para pruebas en Swagger u otras herramientas, se puede forzar el tenant por:
            // a) Header (útil desde Swagger UI)
            if (http.Request.Headers.TryGetValue("X-Tenant-Slug", out var h) && !StringValues.IsNullOrEmpty(h))
                return TenantId.From(h.ToString());

            // b) Querystring ?tenant=slug
            if (http.Request.Query.TryGetValue("tenant", out var q) && !StringValues.IsNullOrEmpty(q))
                return TenantId.From(q.ToString());


            // 1) Ruta /t/{slug}
            var path = http.Request.Path.Value ?? string.Empty;
            var m = RouteSlug.Match(path);
            if (m.Success) return TenantId.From(m.Groups["slug"].Value);


            // 2) Header explícito
            if (http.Request.Headers.TryGetValue("X-Tenant-Slug", out var slugHeader) && !string.IsNullOrWhiteSpace(slugHeader))
                return TenantId.From(slugHeader.ToString());


            // 3) Webhook Kommo (form-urlencoded): account_id / scope_id
            if (http.Request.HasFormContentType && http.Request.Form.TryGetValue("account_id", out var acc))
                return MapKommoAccountToTenant(acc!);
            if (http.Request.HasFormContentType && http.Request.Form.TryGetValue("scope_id", out var scope))
                return MapKommoScopeToTenant(scope!);


            // 4) Subdominio → opcional (empresa.kommo.com)
            var host = http.Request.Host.Host;
            var sub = host.Split('.').FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(sub) && sub is not ("localhost" or "127"))
                return TenantId.From(sub);


            // 5) Fallback: se decide en middleware con DefaultTenant
            return TenantId.From(string.Empty);
        }

        // Mapeos específicos de Kommo a TenantId
        private static TenantId MapKommoAccountToTenant(string accountId) => TenantId.From(accountId);
        private static TenantId MapKommoScopeToTenant(string scopeId) => TenantId.From(scopeId);
    }
}
