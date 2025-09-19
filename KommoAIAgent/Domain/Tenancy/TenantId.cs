namespace KommoAIAgent.Domain.Tenancy
{
    /// <summary>
    /// Identificación única de un tenant.7
    /// </summary>
    /// <param name="Value"></param>
    public readonly record struct TenantId(string Value)
    {
        // Normalizamos el valor al crear la instancia.
        public override string ToString() => Value;

        // Conversión implícita a string para facilitar su uso.
        public static implicit operator string(TenantId id) => id.Value;

        // Fábrica para crear TenantId desde string, manejando nulls y espacios.
        public static TenantId From(string? v) => new(v?.Trim().ToLowerInvariant() ?? string.Empty);
    }
}
