namespace Emberward.Tests;

/// <summary>
/// Loops with a bound, for tests that step a world until something happens: a test whose condition never comes fails
/// with a message that says what it waited for, instead of hanging the run.
/// </summary>
internal static class Bounded
{
    /// <summary>
    /// Calls <paramref name="step"/> until <paramref name="done"/> holds, at most <paramref name="limit"/> times, and
    /// fails naming <paramref name="what"/> if it never does. Returns how many steps it took.
    /// </summary>
    internal static int Until(Func<bool> done, Action step, int limit, string what)
    {
        for (int steps = 0; steps < limit; steps++)
        {
            if (done())
                return steps;
            step();
        }
        Assert.True(done(), $"{what}: not reached in {limit} steps");
        return limit;
    }
}
