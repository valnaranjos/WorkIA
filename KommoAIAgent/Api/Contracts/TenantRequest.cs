namespace KommoAIAgent.Api.Contracts
{
    public sealed record TenantRequest(
      string? Slug,
      string DisplayName,
      string KommoBaseUrl,
      string? IaProvider,
      string? IaModel,
      float? Temperature,
      float? TopP,
      int? MaxTokens,
      int? MonthlyTokenBudget,
      int? AlertThresholdPct,
      int? RatePer5Minutes,
    int? ImageCacheTTLMinutes
  );

    public sealed record TenantResponse(
        Guid Id,
        string Slug,
        string DisplayName,
        bool IsActive,
        string KommoBaseUrl,
        string IaProvider,
        string IaModel,
        int MonthlyTokenBudget,
        int AlertThresholdPct,
        DateTime CreatedAt,
        DateTime? UpdatedAt
    );
}
