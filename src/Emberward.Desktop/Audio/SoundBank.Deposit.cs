namespace Emberward.Desktop.Audio;

public sealed partial class SoundBank
{
    /// <summary>
    /// Pyrite banked at the core: a cascade of five crystalline clinks rising in pitch, then a low warm thump as the
    /// core takes it, about 0.6 s.
    /// </summary>
    private static float[] Deposit(ref Noise noise)
    {
        ReadOnlySpan<float> clinks = [1568f, 1760f, 2093f, 2349f, 2637f];
        var samples = Buffer(0.62f);
        var thump = new LowPass();
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            float bells = 0f;
            for (int k = 0; k < clinks.Length; k++)
                bells += Bell(t - 0.055f * k, clinks[k], 0.05f + 0.01f * k) * (0.55f + 0.1f * k);
            // The core taking it: a falling low sine under a soft breath of filtered noise.
            float tt = t - 0.3f;
            float body = 0f;
            if (tt >= 0f)
            {
                phase += Tau * (120f - 50f * (1f - Decay(tt, 0.08f))) / Rate;
                body = MathF.Sin(phase) * Decay(tt, 0.12f) * Attack(tt, 0.01f) * 1.1f
                    + thump.Process(noise.Next(), 500f) * Decay(tt, 0.06f) * 1.4f;
            }
            samples[i] = bells * 0.5f + body;
        }
        return samples;
    }
}
