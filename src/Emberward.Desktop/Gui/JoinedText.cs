namespace Emberward.Desktop.Gui;

/// <summary>
/// Strings the HUD joins every frame from parts that change far less often (a drop line's time and distance, an
/// effect's tag and seconds), kept for as long as the parts are the same strings. The parts come from caches of their
/// own (<see cref="DropBoard"/>, <see cref="HudNumbers"/>), so an unchanged part is the same instance and the test is
/// a reference compare. A handful of entries, reused in turn.
/// </summary>
internal sealed class JoinedText
{
    private const int Size = 16;

    private readonly (string? A, string? Sep, string? B, string? C, string? Joined)[] _entries = new (string?, string?, string?, string?, string?)[Size];
    private int _next;

    /// <summary><paramref name="a"/>, <paramref name="separator"/> and <paramref name="b"/> in one string.</summary>
    public string Join(string a, string separator, string b)
    {
        foreach (var entry in _entries)
            if (ReferenceEquals(entry.A, a) && ReferenceEquals(entry.Sep, separator) && ReferenceEquals(entry.B, b) && entry.C == null)
                return entry.Joined!;
        return Keep(a, separator, b, null, a + separator + b);
    }

    /// <summary>What the arrow at the edge of the view says of <paramref name="line"/> (<see cref="DropLine.PointerText"/>).</summary>
    public string PointerText(in DropLine line)
    {
        // The kind's mark stands in for the separator: the parts alone do not tell two kinds apart.
        string mark = Presentation.PowerupPresentations.Get(line.Kind).MarkText;
        string first = line.State == DropLineState.Landed ? line.Left : line.When;
        foreach (var entry in _entries)
            if (ReferenceEquals(entry.A, mark) && ReferenceEquals(entry.B, first) && ReferenceEquals(entry.C, line.Detail)
                && ReferenceEquals(entry.Sep, PointerKey(line.State)))
                return entry.Joined!;
        return Keep(mark, PointerKey(line.State), first, line.Detail, line.PointerText);
    }

    // Tells a pointer's entry from a join's, and a landed drop's (time left) from a forecast's (time to landing).
    private static string PointerKey(DropLineState state) => state == DropLineState.Landed ? "landed" : "pointer";

    private string Keep(string a, string separator, string b, string? c, string joined)
    {
        _entries[_next] = (a, separator, b, c, joined);
        _next = (_next + 1) % Size;
        return joined;
    }
}
