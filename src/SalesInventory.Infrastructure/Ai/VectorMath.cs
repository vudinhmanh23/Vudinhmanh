using System.Runtime.InteropServices;

namespace SalesInventory.Infrastructure.Ai;

public static class VectorMath
{
    // float[] <-> little-endian float32 bytes (the format stored in KnowledgeChunks.Embedding)
    public static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        MemoryMarshal.AsBytes(vector.AsSpan()).CopyTo(bytes);
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        bytes.AsSpan(0, vector.Length * sizeof(float)).CopyTo(MemoryMarshal.AsBytes(vector.AsSpan()));
        return vector;
    }

    // Cosine similarity in [-1, 1]; 0 when the sizes differ or either vector is all zeros
    public static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0;
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
