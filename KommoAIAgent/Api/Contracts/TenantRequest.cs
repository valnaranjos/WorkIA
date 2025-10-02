using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace KommoAIAgent.Api.Contracts
{
    /// <summary>
    /// DTO de creación/actualización de tenant
    /// </summary>
    /// <param name="Slug"></param>
    /// <param name="DisplayName"></param>
    /// <param name="KommoBaseUrl"></param>
    /// <param name="IaProvider"></param>
    /// <param name="IaModel"></param>
    /// <param name="Temperature"></param>
    /// <param name="TopP"></param>
    /// <param name="MaxTokens"></param>
    /// <param name="MonthlyTokenBudget"></param>
    /// <param name="AlertThresholdPct"></param>
    /// <param name="RatePer5Minutes"></param>
    /// <param name="ImageCacheTTLMinutes"></param>
    /// <param name="SystemPrompt"></param>
    /// <param name="BusinessRulesJson"></param>
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
    int? ImageCacheTTLMinutes,
    string? SystemPrompt,
    string? BusinessRulesJson
  );

    /// <summary>
    /// DTO de respuesta con datos de un tenant
    /// </summary>
    /// <param name="Id"></param>
    /// <param name="Slug"></param>
    /// <param name="DisplayName"></param>
    /// <param name="IsActive"></param>
    /// <param name="KommoBaseUrl"></param>
    /// <param name="IaProvider"></param>
    /// <param name="IaModel"></param>
    /// <param name="MonthlyTokenBudget"></param>
    /// <param name="AlertThresholdPct"></param>
    /// <param name="CreatedAt"></param>
    /// <param name="UpdatedAt"></param>
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

    /// <summary>
    /// DTO para actualizar el prompt del sistema de un tenant.
    /// </summary>
    public sealed class UpdatePromptRequest
    {
        public string SystemPrompt { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO para actualizar las reglas de negocio (JSON) de un tenant.
    /// </summary>
    public sealed class UpdateRulesRequest
    {
        [Required]
        public JsonElement Rules { get; set; }
    }
}
