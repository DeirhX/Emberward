namespace Emberward.Core.Terrain;

using Emberward.Core.Types;

public partial class TerrainGrid
{
    // Side of the tiles ore cells are counted in, as a power of two.
    private const int OreTileShift = 5;

    // Ore cells (plain and core) per tile, kept by every pixel write, so that a place with no ore is known without
    // looking at its cells.
    private int[] _oreCellsOfTile = [];
    private int _oreTilesX;

    /// <summary>
    /// Whether the circle of <paramref name="radius"/> around <paramref name="center"/> holds plain ore
    /// (<paramref name="plain"/>) and lump core (<paramref name="core"/>). Where the tiles under it hold no ore at
    /// all, which is most of the map, no cell is read.
    /// </summary>
    public void FindOre(Position center, int radius, out bool plain, out bool core)
    {
        plain = core = false;
        int minX = Math.Max(0, center.X - radius), maxX = Math.Min(Width - 1, center.X + radius);
        int minY = Math.Max(0, center.Y - radius), maxY = Math.Min(Height - 1, center.Y + radius);
        if (maxX < minX || maxY < minY)
            return;

        if (!AnyOreInTiles(minX, minY, maxX, maxY))
            return;

        for (int y = minY; y <= maxY; y++)
        {
            int dy = y - center.Y;
            for (int x = minX; x <= maxX; x++)
            {
                int dx = x - center.X;
                if (dx * dx + dy * dy > radius * radius)
                    continue;
                TerrainPixel pixel = _data[x + y * Width];
                if (Pixel.IsOreCore(pixel))
                    core = true;
                else if (Pixel.IsOre(pixel))
                    plain = true;
            }
        }
    }

    /// <summary>
    /// The plain ore cell (not lump core) within <paramref name="radius"/> of <paramref name="center"/> nearest
    /// <paramref name="near"/> (ties to the first in row order), for a digger choosing where to dig next. Like
    /// <see cref="FindOre"/>, it reads no cell where the tiles under the circle hold no ore.
    /// </summary>
    public bool TryFindPlainOre(Position center, int radius, Position near, out Position cell)
    {
        cell = default;
        int minX = Math.Max(0, center.X - radius), maxX = Math.Min(Width - 1, center.X + radius);
        int minY = Math.Max(0, center.Y - radius), maxY = Math.Min(Height - 1, center.Y + radius);
        if (maxX < minX || maxY < minY || !AnyOreInTiles(minX, minY, maxX, maxY))
            return false;

        long best = long.MaxValue;
        for (int y = minY; y <= maxY; y++)
        {
            int dy = y - center.Y;
            for (int x = minX; x <= maxX; x++)
            {
                int dx = x - center.X;
                if (dx * dx + dy * dy > radius * radius)
                    continue;
                TerrainPixel pixel = _data[x + y * Width];
                if (!Pixel.IsOre(pixel) || Pixel.IsOreCore(pixel))
                    continue;
                long nx = x - near.X, ny = y - near.Y;
                long distance = nx * nx + ny * ny;
                if (distance < best)
                {
                    best = distance;
                    cell = new Position(x, y);
                }
            }
        }
        return best != long.MaxValue;
    }

    /// <summary>Whether any ore tile overlaps the cell rectangle from (<paramref name="minX"/>, <paramref name="minY"/>) to (<paramref name="maxX"/>, <paramref name="maxY"/>).</summary>
    private bool AnyOreInTiles(int minX, int minY, int maxX, int maxY)
    {
        for (int ty = minY >> OreTileShift; ty <= maxY >> OreTileShift; ty++)
            for (int tx = minX >> OreTileShift; tx <= maxX >> OreTileShift; tx++)
                if (_oreCellsOfTile[tx + ty * _oreTilesX] > 0)
                    return true;
        return false;
    }

    private void NoteOreWrite(int offset, TerrainPixel was, TerrainPixel now)
    {
        bool wasOre = Pixel.IsOre(was), isOre = Pixel.IsOre(now);
        if (wasOre != isOre)
            _oreCellsOfTile[OreTileOf(offset)] += isOre ? 1 : -1;
    }

    /// <summary>Counts the ore from the grid, after pixels were written in bulk.</summary>
    private void RecountOre()
    {
        if (_oreCellsOfTile.Length == 0)
        {
            _oreTilesX = (Width >> OreTileShift) + 1;
            _oreCellsOfTile = new int[_oreTilesX * ((Height >> OreTileShift) + 1)];
        }
        Array.Clear(_oreCellsOfTile);
        for (int offset = 0; offset < _data.Length; offset++)
            if (Pixel.IsOre(_data[offset]))
                _oreCellsOfTile[OreTileOf(offset)]++;
    }

    private int OreTileOf(int offset) => (offset % Width >> OreTileShift) + (offset / Width >> OreTileShift) * _oreTilesX;

    /// <summary>
    /// Level generation: turns one cell of solid ground into ore (<see cref="LevelGen.OreDeposits"/>). Specks only
    /// replace dirt (<paramref name="dirtOnly"/>); pockets and lumps also replace plain rock. The cell is seeded with
    /// heat at once: <see cref="Config.Tweaks.Ore.CoreStartTemperature"/> for a core cell, <see cref="Config.Tweaks.Ore.PlainStartTemperature"/>
    /// otherwise, booked in the <see cref="Ledger"/> as injected. That heat is finite: ore is ordinary stone, so it
    /// cools. Returns false (and changes nothing) for any other cell or one on the border margin.
    /// </summary>
    public bool PlaceOre(Position pos, bool core, bool dirtOnly)
    {
        if (pos.X < 1 || pos.Y < 1 || pos.X >= Width - 1 || pos.Y >= Height - 1)
            return false;
        TerrainPixel existing = _data[pos.X + pos.Y * Width];
        bool fits = dirtOnly ? Pixel.IsSoil(existing) : Pixel.IsSolidGround(existing);
        if (!fits)
            return false;

        if (core)
            SeedHeatedPixel(pos, TerrainPixel.PyriteCore, Config.Tweaks.Ore.CoreStartTemperature);
        else
            SeedHeatedPixel(pos, TerrainPixel.Pyrite, Config.Tweaks.Ore.PlainStartTemperature);
        return true;
    }
}
