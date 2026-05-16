namespace _025_ResumeParserCandidateRanker.Services;

/// <summary>
/// Computes cosine similarity between two equal-length embedding vectors.
/// </summary>
public class SimilarityService
{
    /// <summary>
    /// Returns a value in [0, 1] representing how similar the two vectors are,
    /// where 1 is identical direction and 0 is orthogonal.
    /// </summary>
    public float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException(
                $"Vectors must have equal dimensions (got {a.Length} and {b.Length}).");

        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0f || normB == 0f) return 0f;
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
