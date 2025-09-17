using KommoAIAgent.Helpers;
using KommoAIAgent.Models;
using KommoAIAgent.Services.Interfaces;
using System.Collections.Concurrent;

namespace KommoAIAgent.Services
{
    /// <summary>
    /// Memoria temporal en memoria para bufferizar mensajes entrantes por lead, con el fin de permitir al cliente responder varios mensajes de corrido antes que responda inmediatamente.
    /// </summary>
    internal sealed class InMemoryMessageBuffer : IMessageBuffer
    {
        private class State
        {
            public readonly List<string> Texts = [];
            public readonly List<AttachmentInfo> Attachments = [];
            public DateTimeOffset FirstTs;
            public DateTimeOffset LastTs;
            public CancellationTokenSource? Cts; // para reprogramar el flush
        }

        private readonly ConcurrentDictionary<long, State> _states = new();
        private readonly ILogger<InMemoryMessageBuffer> _logger;
        private readonly TimeSpan _window;
        private readonly TimeSpan _maxBurst;


        public InMemoryMessageBuffer(IConfiguration cfg, ILogger<InMemoryMessageBuffer> logger)
        {
            _logger = logger;
            var w = int.TryParse(cfg["Debounce:WindowMs"], out var ms) ? ms : 2000;
            var b = int.TryParse(cfg["Debounce:MaxBurstMs"], out var bs) ? bs : 8000;
            _window = TimeSpan.FromMilliseconds(w);
            _maxBurst = TimeSpan.FromMilliseconds(b);
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
            var state = _states.GetOrAdd(leadId, _ => new State());
            bool flushNow = false;
            AggregatedMessage? aggregateToSend = null;

            lock (state) // protege contra carreras por lead
            {
                var now = DateTimeOffset.UtcNow;

                if (state.Texts.Count == 0)
                    state.FirstTs = now;

                // Acumula texto si trae algo útil
                if (!string.IsNullOrWhiteSpace(text))
                    state.Texts.Add(text!);

                // Acumula adjuntos (evita duplicados por url simple)
                foreach (var a in attachments)
                {
                    if (!string.IsNullOrWhiteSpace(a.Url) &&
                        !state.Attachments.Any(x => string.Equals(x.Url, a.Url, StringComparison.OrdinalIgnoreCase)))
                    {
                        state.Attachments.Add(a);
                    }
                }

                state.LastTs = now;

                // Regla: si llega IMAGEN -> flush inmediato
                if (state.Attachments.Any(AttachmentHelper.IsImage))
                {
                    flushNow = true;
                }
                else
                {
                    // Si no, decidimos por tiempos
                    var burstAge = now - state.FirstTs;
                    if (burstAge >= _maxBurst)
                        flushNow = true;
                }

                // Cancelar temporizador anterior y programar nuevo
                state.Cts?.Cancel();
                state.Cts?.Dispose();
                state.Cts = new CancellationTokenSource();

                var delay = flushNow ? TimeSpan.Zero : _window;

                // Prepara aggregate (copia defensiva) para el callback
                aggregateToSend = new AggregatedMessage(
                    Text: string.Join(" ", state.Texts).Trim(),
                    Attachments: [.. state.Attachments]
                );

                // Si flush inmediato, limpia ya el estado; si no, se limpia al disparar
                if (flushNow)
                {
                    state.Texts.Clear();
                    state.Attachments.Clear();
                    state.FirstTs = state.LastTs = default;
                }

                // Lanza tarea de flush (0ms o ventana)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delay, state.Cts.Token);
                        // Si el delay expiró sin cancelación y NO era flush inmediato,
                        // necesitamos tomar snapshot y limpiar aquí.
                        if (!flushNow)
                        {
                            AggregatedMessage aggregate;
                            lock (state)
                            {
                                aggregate = new AggregatedMessage(
                                    Text: string.Join(" ", state.Texts).Trim(),
                                    Attachments: state.Attachments.ToList()
                                );
                                state.Texts.Clear();
                                state.Attachments.Clear();
                                state.FirstTs = state.LastTs = default;
                            }
                            await onFlush(leadId, aggregate);
                        }
                        else
                        {
                            // flushNow: ya snapshotteado y limpiado; usa aggregate preparado
                            await onFlush(leadId, aggregateToSend!);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // reprogramación
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error al hacer flush del buffer (LeadId={LeadId})", leadId);
                    }
                }, CancellationToken.None);
            }

            await Task.CompletedTask;
        }


        /// <summary>
        /// limpiar el estado del buffer para un lead específico.
        /// </summary>
        /// <param name="leadId"></param>
        public void Clear(long leadId)
        {
            if (_states.TryRemove(leadId, out var state))
            {
                lock (state)
                {
                    // Si usara Timer para el flush diferido:
                    //state.Timer?.Change(Timeout.Infinite, Timeout.Infinite);
                    //state.Timer?.Dispose();

                    //CancellationTokenSource para el delay:
                    state.Cts?.Cancel();
                    state.Cts?.Dispose();

                    state.Texts.Clear();
                    state.Attachments.Clear();
                }
            }
        }
    }
}
