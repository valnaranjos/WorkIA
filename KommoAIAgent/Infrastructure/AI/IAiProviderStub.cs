namespace KommoAIAgent.Infrastructure.AI;

/// <summary>
/// Interfaz base para futuros providers de IA.
/// NOTA: Por ahora solo OpenAI está implementado.
/// Para agregar nuevos providers (Anthropic, Gemini, etc.):
/// 1. Crear clase que implemente esta interfaz
/// 2. Registrarla en Program.cs
/// 3. Actualizar lógica en OpenAiService o crear factory
/// </summary>
public interface IAiProviderStub
{
    string ProviderName { get; }
    Task<string> GenerateResponseAsync(string prompt, CancellationToken ct = default);
    bool IsConfiguredFor(string tenantSlug);
}

/// <summary>
/// Placeholder para futuros providers.
/// Cuando necesites agregar Anthropic, crea: AnthropicProvider : IAiProviderStub
/// </summary>
public class FutureProviderPlaceholder
{
    // TODO: Implementar cuando se necesite otro provider
    // Ejemplo: AnthropicProvider, GeminiProvider, etc.
}