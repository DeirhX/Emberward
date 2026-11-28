namespace Emberward.Tests;

using Emberward.Protocol;

/// <summary>
/// When a client plays the bundles that have reached it: evenly, with a cushion as deep as the link's unevenness
/// needs, and a late bundle the moment it comes, never two at once to make up for it.
/// </summary>
public class PlayoutPacerTests
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1.0 / 24);

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    private static PlayoutPacer Pacer() => new(Step);

    /// <summary>Feeds ticks from <paramref name="first"/> arriving on time, each <paramref name="delay"/> after it leaves.</summary>
    private static void Steady(PlayoutPacer pacer, int first, int count, TimeSpan delay)
    {
        for (int tick = first; tick < first + count; tick++)
            pacer.Arrived(tick, Step * tick + delay);
    }

    [Fact]
    public void ASteadyLink_KeepsNothingInHand()
    {
        var pacer = Pacer();
        Steady(pacer, 0, 200, Ms(80));

        Assert.Equal(0, pacer.TargetDepth);
        Assert.Equal(TimeSpan.Zero, pacer.Jitter);
    }

    [Fact]
    public void ABurstyLink_KeepsEnoughInHandToRideItOut()
    {
        var pacer = Pacer();
        Steady(pacer, 0, 50, Ms(80));
        // Five ticks held back by the link, then delivered together with the sixth: the first was 6 steps late.
        for (int tick = 50; tick < 56; tick++)
            pacer.Arrived(tick, Step * 56 + Ms(80));

        // The worst arrival is six steps behind the best; the cushion that would take is more than the cap.
        Assert.Equal(6 * Step.TotalMilliseconds, pacer.Jitter.TotalMilliseconds, precision: 1);
        Assert.Equal(PlayoutPacer.MaxDepth, pacer.TargetDepth);
    }

    [Fact]
    public void AModeratelyUnevenLink_KeepsOneOrTwo()
    {
        var pacer = Pacer();
        for (int tick = 0; tick < 100; tick++)
            pacer.Arrived(tick, Step * tick + Ms(tick % 4 == 0 ? 130 : 80)); // every fourth 50 ms late

        Assert.Equal(2, pacer.TargetDepth); // (50 - 8) / 41.7 rounds up to 2
    }

    [Fact]
    public void TheCushion_ShrinksOneBundleASecond_OnceTheLinkSettles()
    {
        var pacer = Pacer();
        Steady(pacer, 0, 10, Ms(80));
        pacer.Arrived(10, Step * 10 + Ms(200));
        int raised = pacer.TargetDepth;
        Assert.True(raised >= 3, $"A 120 ms late tick should raise the target, got {raised}.");

        // Ten seconds of a steady link push the late tick out of the window (at tick 250). The target does not drop
        // at once but a bundle a second, so a backlog drains gently.
        Steady(pacer, 11, 250, Ms(80)); // to tick 260
        Assert.Equal(raised, pacer.TargetDepth);
        Steady(pacer, 261, 25, Ms(80)); // to tick 285, a second and a half after the late tick left
        Assert.Equal(raised - 1, pacer.TargetDepth);
        Steady(pacer, 286, 24 * 5, Ms(80));
        Assert.Equal(0, pacer.TargetDepth);
    }

    [Fact]
    public void APause_IsNotJitter()
    {
        var pacer = Pacer();
        Steady(pacer, 0, 50, Ms(80));
        // The relay held the match for five seconds: every tick after it leaves that much later by the client's clock.
        Steady(pacer, 50, 50, Ms(80) + TimeSpan.FromSeconds(5));

        Assert.Equal(0, pacer.TargetDepth);
    }

    [Fact]
    public void StepLength_IsTheRelaysStep_WhileTheCushionIsLevel()
    {
        var pacer = Pacer();
        for (int i = 0; i < 50; i++)
            pacer.Stepped(1);

        Assert.Equal(Step, pacer.StepLength());
    }

    [Fact]
    public void StepLength_IsShorter_WhileABacklogWaits_AndLonger_WhileTheCushionIsShort()
    {
        var behind = Pacer();
        for (int i = 0; i < 50; i++)
            behind.Stepped(4);
        var starved = Pacer();
        Steady(starved, 0, 10, Ms(80));
        starved.Arrived(10, Step * 10 + Ms(160)); // wants a cushion now
        for (int i = 0; i < 50; i++)
            starved.Stepped(1);

        Assert.True(behind.StepLength() < Step * 0.8, $"Behind by three, a step took {behind.StepLength()}.");
        Assert.True(behind.StepLength() >= Step * PlayoutPacer.FastestScale);
        Assert.True(starved.StepLength() > Step, $"Short of the cushion, a step took {starved.StepLength()}.");
        Assert.True(starved.StepLength() <= Step * PlayoutPacer.SlowestScale);
    }

    [Fact]
    public void StepsDue_PlaysOneAStep()
    {
        var pacer = Pacer();
        Assert.Equal(1, pacer.StepsDue(Ms(0), queued: 1, maxSteps: 4)); // the first is owed from the start
        Assert.Equal(0, pacer.StepsDue(Ms(20), queued: 1, maxSteps: 4));
        Assert.Equal(1, pacer.StepsDue(Step + Ms(1), queued: 1, maxSteps: 4));
    }

    [Fact]
    public void ALateBundle_IsPlayedTheMomentItComes_AndNoSecondFollowsAtOnce()
    {
        var pacer = Pacer();
        Assert.Equal(1, pacer.StepsDue(Ms(0), queued: 1, maxSteps: 4));
        // Nothing arrives for three steps' time: the step due is owed, the time beyond it is not.
        Assert.Equal(0, pacer.StepsDue(Step * 3, queued: 0, maxSteps: 4));
        Assert.Equal(1f, pacer.Progress);
        // Two arrive at once a moment later: one is played now, and the other a step later, not with it.
        Assert.Equal(1, pacer.StepsDue(Step * 3 + Ms(2), queued: 2, maxSteps: 4));
        Assert.True(pacer.Progress < 0.1f);
        Assert.Equal(0, pacer.StepsDue(Step * 3 + Ms(20), queued: 1, maxSteps: 4));
    }

    [Fact]
    public void ABacklog_DrainsBySteps_ThatComeSooner()
    {
        var pacer = Pacer();
        TimeSpan now = TimeSpan.Zero;
        int queued = 6, played = 0, frames = 0;
        // Frames at 120 Hz; no new bundle arrives, so only the six in hand are played.
        while (queued > 0 && frames < 1000)
        {
            int steps = pacer.StepsDue(now, queued, maxSteps: 4);
            Assert.True(steps <= 1, $"Frame {frames} played {steps} steps at once.");
            queued -= steps;
            played += steps;
            now += Ms(1000.0 / 120);
            frames++;
        }

        Assert.Equal(6, played);
        // Faster than six of the relay's steps: the backlog drains early.
        Assert.True(now < Step * 6, $"Six steps took {now.TotalMilliseconds:F0} ms.");
    }

    /// <summary>
    /// A Wi-Fi-like link: 60 ms each way with a few ms of noise, and now and then a hiccup that holds every packet
    /// back for up to 150 ms and then delivers them together (TCP keeps them in order). Played by the pacer at 120
    /// frames a second, the steps come one a frame at most, and once the cushion is built a stall is rare: the
    /// tank and the camera move evenly instead of stopping and jumping with every hiccup.
    /// </summary>
    [Fact]
    public void AHiccuppingLink_IsPlayedEvenly()
    {
        var random = new Random(7);
        const int ticks = 24 * 40;
        var arrivals = new TimeSpan[ticks];
        TimeSpan previous = TimeSpan.Zero;
        TimeSpan hiccupUntil = TimeSpan.Zero;
        for (int tick = 0; tick < ticks; tick++)
        {
            TimeSpan sent = Step * tick;
            if (random.NextDouble() < 1.0 / 72) // about every three seconds
                hiccupUntil = sent + Ms(random.Next(60, 150));
            TimeSpan arrival = sent + Ms(60 + random.Next(0, 6));
            if (arrival < hiccupUntil)
                arrival = hiccupUntil;
            arrivals[tick] = previous = arrival > previous ? arrival : previous;
        }

        var pacer = Pacer();
        int arrived = 0, played = 0, stalledFrames = 0, maxStepsInAFrame = 0;
        TimeSpan frame = Ms(1000.0 / 120);
        TimeSpan settled = TimeSpan.FromSeconds(10);
        for (TimeSpan now = TimeSpan.Zero; played < ticks - 1 && now < TimeSpan.FromSeconds(60); now += frame)
        {
            while (arrived < ticks && arrivals[arrived] <= now)
                pacer.Arrived(arrived, arrivals[arrived++]);
            int steps = pacer.StepsDue(now, arrived - played, maxSteps: 4);
            played += steps;
            maxStepsInAFrame = Math.Max(maxStepsInAFrame, steps);
            if (now > settled && steps == 0 && arrived == played && pacer.Progress >= 1f)
                stalledFrames++;
        }

        Assert.Equal(1, maxStepsInAFrame);
        // Thirty seconds after the first ten is 3600 frames; a few owed frames around the worst hiccup are allowed.
        Assert.True(stalledFrames < 10, $"{stalledFrames} frames waited on a late tick.");
        Assert.True(pacer.TargetDepth <= PlayoutPacer.MaxDepth, $"depth {pacer.TargetDepth}");
    }
}
