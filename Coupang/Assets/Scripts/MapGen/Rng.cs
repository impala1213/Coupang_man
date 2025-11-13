using System;
using UnityEngine;

public class Rng
{
    private System.Random random;

    public Rng(int seed)
    {
        random = new System.Random(seed);
    }

    public int NextInt()
    {
        return random.Next();
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        return random.Next(minInclusive, maxExclusive);
    }

    public float NextFloat()
    {
        return (float)random.NextDouble();
    }

    public float NextFloat(float minInclusive, float maxInclusive)
    {
        return minInclusive + (float)random.NextDouble() * (maxInclusive - minInclusive);
    }

    public Vector2 NextInsideUnitCircle()
    {
        float angle = NextFloat(0f, Mathf.PI * 2f);
        float radius = Mathf.Sqrt(NextFloat());
        return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
    }

    public Rng Split(int salt)
    {
        unchecked
        {
            int newSeed = random.Next() ^ (salt * 73856093);
            return new Rng(newSeed);
        }
    }
}
