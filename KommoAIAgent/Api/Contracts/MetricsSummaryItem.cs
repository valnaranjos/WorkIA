namespace KommoAIAgent.Api.Contracts;

// Resumen por provider/model en un rango
public sealed record MetricsSummaryItem(
    string Provider,
    string Model,
    long Calls,
    long InputTokens,
    long OutputTokens,
    long EmbeddingChars,
    long Errors,
    decimal EstimatedUsd
);

// Resumen completo
public sealed record MetricsSummaryResponse(
    string Tenant,
    DateTime From,
    DateTime To,
    IReadOnlyList<MetricsSummaryItem> Items,
    decimal EstimatedTotalUsd
);

// Serie diaria agregada
public sealed record DailyUsageItem(
    DateTime Date,
    long Calls,
    long InputTokens,
    long OutputTokens,
    long EmbeddingChars,
    long Errors,
    decimal EstimatedUsd
);

// Serie diaria completa
public sealed record DailyUsageResponse(
    string Tenant,
    DateTime From,
    DateTime To,
    IReadOnlyList<DailyUsageItem> Days,
    decimal EstimatedTotalUsd
);

// Últimos errores
public sealed record UsageErrorItem(
    DateTime WhenUtc,
    string Provider,
    string Model,
    string Operation,
    string Message
);

// Respuesta con lista de errores
public sealed record UsageErrorsResponse(
    string Tenant,
    int Count,
    IReadOnlyList<UsageErrorItem> Items
);
