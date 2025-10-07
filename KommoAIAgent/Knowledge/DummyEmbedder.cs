using System.Security.Cryptography;
using System.Text;

namespace KommoAIAgent.Knowledge;

public sealed class DummyEmbedder : IEmbedder
{
    public int Dimensions => 16;
    public string Model => "dummy-embedder";

    public Task<float[]> EmbedTextAsync(string text, CancellationToken ct = default)
        => Task.FromResult(HashToVec(text));

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        => Task.FromResult(texts.Select(HashToVec).ToArray());

    private static float[] HashToVec(string s)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
        var vec = new float[16];
        for (int i = 0; i < 16; i++) vec[i] = (hash[i] / 255f) * 2f - 1f;
        return vec;
    }
}
