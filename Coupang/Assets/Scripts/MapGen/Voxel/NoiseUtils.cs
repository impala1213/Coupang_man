using UnityEngine;

/// <summary>
/// Helper methods for noise-based terrain generation.
/// Uses Unity's 2D PerlinNoise under the hood and combines it
/// to approximate 3D-like noise for caves.
/// </summary>
public static class NoiseUtils
{
    /// <summary>
    /// Simple fractal 3D noise using a combination of 2D PerlinNoise samples.
    /// Returns a value in [0, 1].
    /// 
    /// This is NOT a true Perlin3D implementation, but it is good enough
    /// to create interesting cave patterns without extra complexity.
    /// </summary>
    public static float FractalPseudoPerlin3D(
        float x, float y, float z,
        int octaves,
        float baseFrequency,
        float lacunarity,
        float persistence)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = baseFrequency;
        float maxValue = 0f;

        for (int o = 0; o < octaves; o++)
        {
            // Combine three different 2D projections: (x,z), (x,y), (y,z).
            float n1 = Mathf.PerlinNoise(x * frequency, z * frequency);
            float n2 = Mathf.PerlinNoise(x * frequency, y * frequency);
            float n3 = Mathf.PerlinNoise(y * frequency, z * frequency);

            float n = (n1 + n2 + n3) / 3f; // average in [0,1]

            value += n * amplitude;
            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        if (maxValue > 0f)
            value /= maxValue;

        return value; // 0..1
    }
}
