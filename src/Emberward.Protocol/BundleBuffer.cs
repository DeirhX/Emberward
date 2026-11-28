namespace Emberward.Protocol;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// A client's bundles that have arrived and not yet been simulated, in tick order. When to simulate them is the
/// <see cref="PlayoutPacer"/>'s to say.
/// </summary>
public sealed class BundleBuffer
{
    private readonly Queue<TickBundle> _bundles = new();

    /// <param name="firstTick">The tick of the first bundle: 0, or for a player who joined in progress, the tick of the snapshot it loaded.</param>
    public BundleBuffer(int firstTick = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstTick);
        NextTick = firstTick;
    }

    /// <summary>The tick the next added bundle must carry.</summary>
    public int NextTick { get; private set; }

    public int Count => _bundles.Count;

    /// <summary>Adds the next bundle. The connection is ordered, so any other tick means a broken stream.</summary>
    public void Add(TickBundle bundle)
    {
        if (bundle.Tick != NextTick)
            throw new InvalidDataException($"Expected the bundle for tick {NextTick}, got tick {bundle.Tick}.");
        _bundles.Enqueue(bundle);
        NextTick++;
    }

    public bool TryTake([NotNullWhen(true)] out TickBundle? bundle) => _bundles.TryDequeue(out bundle);
}
