using KommoAIAgent.Application.Common;
using KommoAIAgent.Application.Interfaces;
using System.Collections.Concurrent;

namespace KommoAIAgent.Infrastructure.Services
{
    /// <summary>
    /// Memoria temporal en memoria para bufferizar mensajes entrantes por lead y x tenant, con el fin de permitir al cliente responder varios mensajes de corrido antes que responda inmediatamente.
    /// </summary>
    internal sealed class InMemoryMessageBuffer : IMessageBuffer
    {
        private sealed class State : IDisposable
        {
            public readonly List<string> Texts = [];
            public readonly List<AttachmentInfo> Attachments = [];
            public DateTimeOffset FirstTs;
            public DateTimeOffset LastTs;
            public CancellationTokenSource? Cts; // para reprogramar el flush

            private Timer? _timer;
            private readonly object _lock = new();
            private bool _disposed;

            public void ScheduleFlush(TimeSpan delay, Func<Task> callback)
            {
                lock (_lock)
                {
                    if (_disposed) return;

                    _timer?.Dispose();

                    if (delay == TimeSpan.Zero)
                    {
                        // Flush inmediato en ThreadPool
                        _ = Task.Run(callback);
                    }
                    else
                    {
                        _timer = new Timer(async _ =>
                        {
                            try { await callback(); }
                            catch { /* ya logueado en el callback */ }
                        }, null, delay, Timeout.InfiniteTimeSpan);
                    }
                }
            }

            public void CancelFlush()
            {
                lock (_lock)
                {
                    _timer?.Dispose();
                    _timer = null;
                }
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    _disposed = true;
                    _timer?.Dispose();
                    _timer = null;
                }
            }
        }

        private readonly ConcurrentDictionary<string, State> _states = new();
        private readonly ITenantContextAccessor _tenant;
        private readonly ILogger<InMemoryMessageBuffer> _logger;
        private readonly TimeSpan _window;
        private readonly TimeSpan _maxBurst;
        private bool _disposed;

        //Obtiene el tenant actual
        private string Key(long leadId)
        {
            var slug = _tenant.Current.Config.Slug ?? "_";
            return $"{slug}:{leadId}";
        }


        public InMemoryMessageBuffer(IConfiguration cfg,
            ILogger<InMemoryMessageBuffer> logger,
            ITenantContextAccessor tenant)
        {
            _logger = logger;
            //Trae la configuración de ventana y ráfaga máxima desde configuración, por defecto 2s y 8s.
            var w = int.TryParse(cfg["Debounce:WindowMs"], out var ms) ? ms : 2000;
            var b = int.TryParse(cfg["Debounce:MaxBurstMs"], out var bs) ? bs : 8000;
            _window = TimeSpan.FromMilliseconds(w);
            _maxBurst = TimeSpan.FromMilliseconds(b);
            _tenant = tenant;
        }


        /// <summary>
        /// Obtiene el estado actual del buffer para un lead específico, y ofrece el buffer para enviarlo.
        /// </summary>
        /// <param name="leadId"></param>
        /// <param name="text"></param>
        /// <param name="attachments"></param>
        /// <param name="onFlush"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async Task OfferAsync(
     long leadId,
     string? text,
     IReadOnlyList<AttachmentInfo> attachments,
     Func<long, AggregatedMessage, Task> onFlush,
     CancellationToken ct = default)
        {
            var state = _states.GetOrAdd(Key(leadId), _ => new State());

            AggregatedMessage? messageToFlush = null;
            TimeSpan flushDelay;
            bool shouldFlush = false;

            // 🔒 Lock REDUCIDO - solo para leer/modificar estado
            lock (state)
            {
                var now = DateTimeOffset.UtcNow;

                if (state.Texts.Count == 0 && state.Attachments.Count == 0)
                    state.FirstTs = now;

                // Acumula texto
                if (!string.IsNullOrWhiteSpace(text))
                    state.Texts.Add(text!);

                // Acumula adjuntos (sin duplicados)
                foreach (var a in attachments)
                {
                    if (!string.IsNullOrWhiteSpace(a.Url) &&
                        !state.Attachments.Any(x => string.Equals(x.Url, a.Url, StringComparison.OrdinalIgnoreCase)))
                    {
                        state.Attachments.Add(a);
                    }
                }

                state.LastTs = now;

                // Decidir flush
                bool hasImage = state.Attachments.Any(AttachmentHelper.IsImage);
                var burstAge = now - state.FirstTs;

                shouldFlush = hasImage || (burstAge >= _maxBurst);

                if (shouldFlush)
                {
                    // Preparar mensaje para flush
                    messageToFlush = new AggregatedMessage(
                        Text: string.Join(" ", state.Texts).Trim(),
                        Attachments: [.. state.Attachments]
                    );

                    // Limpiar estado
                    state.Texts.Clear();
                    state.Attachments.Clear();
                    state.FirstTs = state.LastTs = default;
                }

                flushDelay = shouldFlush ? TimeSpan.Zero : _window;
            }
            // 🔓 Lock liberado ANTES de programar flush

            // Programar flush FUERA del lock
            state.ScheduleFlush(flushDelay, async () =>
            {
                AggregatedMessage finalMessage;

                // Si NO fue flush inmediato, tomar snapshot ahora
                if (!shouldFlush)
                {
                    lock (state)
                    {
                        finalMessage = new AggregatedMessage(
                            Text: string.Join(" ", state.Texts).Trim(),
                            Attachments: [.. state.Attachments]
                        );
                        state.Texts.Clear();
                        state.Attachments.Clear();
                        state.FirstTs = state.LastTs = default;
                    }
                }
                else
                {
                    finalMessage = messageToFlush!;
                }

                // Ejecutar callback sin lock
                try
                {
                    await onFlush(leadId, finalMessage);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en flush callback (lead={LeadId})", leadId);
                }
            });

            await Task.CompletedTask;
        }


        /// <summary>
        /// limpiar el estado del buffer para un lead específico.
        /// </summary>
        /// <param name="leadId"></param>
        public void Clear(long leadId)
        {
            if (_states.TryRemove(Key(leadId), out var state))
            {
                state.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kvp in _states)
            {
                kvp.Value.Dispose();
            }
            _states.Clear();
        }
    }
}
