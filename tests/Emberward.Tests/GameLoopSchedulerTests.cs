namespace Emberward.Tests;

using Emberward.Desktop;

/// <summary>
/// The desktop's step clock as a network match uses it: a step whose bundle is late is owed, not dropped and not
/// doubled, and frames hold at the latest step while it is.
/// </summary>
public class GameLoopSchedulerTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(40);

    [Fact]
    public void AStalledStep_IsOwedOnce_AndHoldsTheFrameAtTheLatestStep()
    {
        var scheduler = new GameLoopScheduler(Step, maxCatchUpSteps: 4);
        scheduler.AddElapsed(Step * 3);

        // Three steps' time passed with no bundle: one step is owed, the rest is dropped.
        scheduler.Stall();
        Assert.True(scheduler.HasReadyStep);
        Assert.Equal(1f, scheduler.StepProgress);

        int taken = scheduler.ConsumeReadySteps(() => { }, () => true);
        Assert.Equal(1, taken);
        Assert.Equal(0f, scheduler.StepProgress);
        Assert.False(scheduler.HasReadyStep);
    }

    [Fact]
    public void AShorterStep_ComesSooner()
    {
        var scheduler = new GameLoopScheduler(Step, maxCatchUpSteps: 4);
        scheduler.AddElapsed(TimeSpan.FromMilliseconds(32));
        Assert.False(scheduler.HasReadyStep);

        scheduler.Step = Step * 0.8;
        Assert.True(scheduler.HasReadyStep);
    }
}
