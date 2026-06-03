using System;

/// <summary>
/// Pure, scene-free math for the audio bubble: decorative waveform heights,
/// progress split, speed cycling, and seek mapping. No UnityEngine deps so it
/// is EditMode-testable (mirrors ScrollFabMath / MediaBubbleSize).
/// </summary>
public static class AudioBubbleMath
{
    public const float MinBarFraction = 0.12f;
    private static readonly float[] SpeedSteps = { 1f, 1.5f, 2f };

    /// Deterministic bar heights in [MinBarFraction, 1] from a stable hash of the seed.
    public static float[] BarHeights(string seed, int barCount)
    {
        if (barCount <= 0) return Array.Empty<float>();

        uint state = Fnv1a(seed);
        var heights = new float[barCount];
        for (int i = 0; i < barCount; i++)
        {
            state = unchecked(state * 1664525u + 1013904223u); // LCG (Numerical Recipes)
            float unit = (state >> 8) / 16777216f;             // top 24 bits -> [0,1)
            heights[i] = MinBarFraction + unit * (1f - MinBarFraction);
        }
        return heights;
    }

    /// Continuous filled-bar position (0..barCount) for the progress fraction (0..1).
    public static float FilledBars(float fraction, int barCount)
    {
        if (barCount <= 0) return 0f;
        float clamped = fraction < 0f ? 0f : (fraction > 1f ? 1f : fraction);
        // n+1 segment mapping: the visible bars finish filling just before playback
        // ends, so the finish event never has to pop the last bar in. floor = fully
        // filled bars; fractional part = the leading bar's partial fill.
        float exact = clamped * (barCount + 1);
        return exact > barCount ? barCount : exact;
    }

    /// Next speed in the cycle (wraps 2x -> 1x). Tolerant of float drift.
    public static float NextSpeed(float current)
    {
        int idx = 0;
        float best = float.MaxValue;
        for (int i = 0; i < SpeedSteps.Length; i++)
        {
            float d = Math.Abs(SpeedSteps[i] - current);
            if (d < best) { best = d; idx = i; }
        }
        return SpeedSteps[(idx + 1) % SpeedSteps.Length];
    }

    /// Scrub fraction (0..1) -> seek target in seconds (clamped to duration).
    public static float SecondsFromFraction(float fraction, int durationSeconds)
    {
        if (durationSeconds <= 0) return 0f;
        float clamped = fraction < 0f ? 0f : (fraction > 1f ? 1f : fraction);
        return clamped * durationSeconds;
    }

    private static uint Fnv1a(string s)
    {
        unchecked
        {
            uint hash = 2166136261u;
            if (!string.IsNullOrEmpty(s))
            {
                for (int i = 0; i < s.Length; i++)
                {
                    hash ^= s[i];
                    hash *= 16777619u;
                }
            }
            return hash;
        }
    }
}
