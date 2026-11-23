namespace Emberward.Desktop.Audio;

/// <summary>The sounds of a timed match and its powerup drops (DESIGN.md §6.8): tones only, each with a shape of its own.</summary>
public sealed partial class SoundBank
{
    /// <summary>The next drop is decided: a soft sonar ping, two notes a fifth apart, easy to ignore.</summary>
    private static float[] DropAnnounce()
    {
        var samples = Buffer(0.5f);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            samples[i] = Blip(t, 659.25f, 0.11f) * 0.8f + Blip(t - 0.12f, 987.77f, 0.16f) * 0.6f;
        }
        return samples;
    }

    /// <summary>A drop has landed: a low thump as it hits the ground under a clear rising chime of three bells, about 0.9 s.</summary>
    private static float[] DropLand(ref Noise noise)
    {
        ReadOnlySpan<float> bells = [783.99f, 1046.5f, 1567.98f];
        var samples = Buffer(0.9f);
        var thud = new LowPass();
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            phase += Tau * (70f + 90f * Decay(t, 0.04f)) / Rate;
            float body = MathF.Sin(phase) * Decay(t, 0.1f) * Attack(t, 0.004f) * 1.2f
                + thud.Process(noise.Next(), 600f) * Decay(t, 0.05f) * 1.2f;
            float chime = 0f;
            for (int k = 0; k < bells.Length; k++)
                chime += Bell(t - 0.06f - 0.11f * k, bells[k], 0.16f + 0.05f * k);
            samples[i] = body + chime * 0.6f;
        }
        return samples;
    }

    /// <summary>Another tank took a drop: two bell notes falling a fourth, the chance gone.</summary>
    private static float[] DropTaken()
    {
        var samples = Buffer(0.5f);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            samples[i] = Bell(t, 880f, 0.1f) + Bell(t - 0.13f, 659.25f, 0.18f);
        }
        return samples;
    }

    /// <summary>The player took a drop: a quick bright arpeggio up a major chord, ending on a held high note.</summary>
    private static float[] DropGained()
    {
        ReadOnlySpan<float> notes = [523.25f, 659.25f, 783.99f, 1046.5f, 1318.51f];
        var samples = Buffer(0.8f);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            float sum = 0f;
            for (int k = 0; k < notes.Length; k++)
                sum += Bell(t - 0.055f * k, notes[k], k == notes.Length - 1 ? 0.28f : 0.07f);
            samples[i] = sum * 0.6f;
        }
        return samples;
    }

    /// <summary>The player's powerup ran out: one tone gliding down an octave, like something winding down.</summary>
    private static float[] EffectEnd()
    {
        var samples = Buffer(0.45f);
        float phase = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            phase += Tau * (880f - 440f * MathF.Min(1f, t / 0.3f)) / Rate;
            samples[i] = (MathF.Sin(phase) + 0.3f * MathF.Sin(3f * phase)) * Attack(t, 0.004f) * Decay(t, 0.16f);
        }
        return samples;
    }

    /// <summary>The player destroyed a tank: two hard high pings close together, over anything else that is playing.</summary>
    private static float[] KillConfirm()
    {
        var samples = Buffer(0.4f);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            samples[i] = Bell(t, 1760f, 0.05f) + Bell(t - 0.08f, 2349.32f, 0.14f);
        }
        return samples;
    }

    /// <summary>A minute left: three even bell strokes on one note.</summary>
    private static float[] ClockWarning()
    {
        var samples = Buffer(0.85f);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            samples[i] = Bell(t, 587.33f, 0.14f) + Bell(t - 0.22f, 587.33f, 0.14f) + Bell(t - 0.44f, 587.33f, 0.22f);
        }
        return samples;
    }

    /// <summary>One of the last seconds: a dry wood-block tick.</summary>
    private static float[] ClockTick(ref Noise noise)
    {
        var samples = Buffer(0.12f);
        var click = new LowPass();
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            samples[i] = Blip(t, 1100f, 0.022f) + click.Process(noise.Next(), 3500f) * Decay(t, 0.004f) * 0.8f;
        }
        return samples;
    }

    /// <summary>Sudden death: an alarm of three buzzes, each a step higher.</summary>
    private static float[] SuddenDeath()
    {
        var samples = Buffer(0.9f);
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            samples[i] = Buzz(t, 0f, 0.2f, 392f) + Buzz(t, 0.25f, 0.45f, 493.88f) + Buzz(t, 0.5f, 0.86f, 622.25f);
        }
        return samples;
    }

    /// <summary>The match is over: a long low horn, two notes a fifth apart with a slow swell, about 1.4 s.</summary>
    private static float[] MatchHorn()
    {
        var samples = Buffer(1.4f);
        var tone = new LowPass();
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)Rate;
            float envelope = Attack(t, 0.08f) * Attack(1.4f - t, 0.35f);
            float saw = 0f;
            // A few harmonics of each note make the brass; the low-pass opens as the horn swells.
            for (int h = 1; h <= 6; h++)
                saw += (MathF.Sin(Tau * 146.83f * h * t) + 0.7f * MathF.Sin(Tau * 220f * h * t)) / h;
            samples[i] = tone.Process(saw, 500f + 1400f * Attack(t, 0.3f)) * envelope;
        }
        return samples;
    }
}
