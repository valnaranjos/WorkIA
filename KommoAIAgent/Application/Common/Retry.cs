using Microsoft.Extensions.Logging;

namespace KommoAIAgent.Infrastructure.Common;

public static class Retry
{
    public static async Task<T> DoAsync<T>(
        Func<Task<T>> action,
        ILogger logger,
        int retries = 3,
        int firstDelayMs = 250)
    {
        var delay = TimeSpan.FromMilliseconds(firstDelayMs);
        for (var attempt = 1; ; attempt++)
        {
            try { return await action(); }
            catch (Exception ex) when (attempt < retries)
            {
                logger.LogWarning(ex, "Retry {Attempt}/{Retries} after {Delay}ms", attempt, retries, delay.TotalMilliseconds);
                await Task.Delay(delay);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2); // backoff exponencial
            }
        }
    }

    public static async Task DoAsync(
        Func<Task> action,
        ILogger logger,
        int retries = 3,
        int firstDelayMs = 250)
        => await DoAsync(async () => { await action(); return true; }, logger, retries, firstDelayMs);
}
