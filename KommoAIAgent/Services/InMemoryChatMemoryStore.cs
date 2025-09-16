using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using KommoAIAgent.Services.Interfaces;

namespace KommoAIAgent.Services
{
    public class InMemoryChatMemoryStore : IChatMemoryStore
    {
        private readonly ConcurrentDictionary<long, LinkedList<(string Role, string Content)>> _mem = new();
        private const int MaxKeep = 20;

        public IReadOnlyList<(string Role, string Content)> Get(long leadId, int maxTurns = 12)
        {
            if (!_mem.TryGetValue(leadId, out var list)) return [];
            return list.Reverse().Take(maxTurns).Reverse().ToList();
        }

        public void AppendUser(long leadId, string content) => Append(leadId, ("user", content));
        public void AppendAssistant(long leadId, string content) => Append(leadId, ("assistant", content));
        public void Clear(long leadId) => _mem.TryRemove(leadId, out _);

        private void Append(long leadId, (string Role, string Content) turn)
        {
            var list = _mem.GetOrAdd(leadId, _ => new LinkedList<(string, string)>());
            list.AddLast(turn);
            while (list.Count > MaxKeep) list.RemoveFirst();
        }
    }
}
