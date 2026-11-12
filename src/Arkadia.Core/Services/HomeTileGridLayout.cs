namespace Arkadia.Core.Services;

public sealed record HomeTileGridItem(string Id, int ColumnSpan = 1, int RowSpan = 1);

public sealed record HomeTileGridPosition(
    string Id,
    int Column,
    int Row,
    int ColumnSpan,
    int RowSpan);

public static class HomeTileGridLayout
{
    public static IReadOnlyList<HomeTileGridPosition> Arrange(
        IReadOnlyList<HomeTileGridItem> tiles,
        int columns)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        var occupied = new HashSet<(int Column, int Row)>();
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var positions = new List<HomeTileGridPosition>(tiles.Count);

        foreach (var tile in tiles)
        {
            if (string.IsNullOrWhiteSpace(tile.Id) || !identifiers.Add(tile.Id))
            {
                throw new ArgumentException("Tile identifiers must be non-empty and unique.", nameof(tiles));
            }

            if (tile.ColumnSpan is < 1 or > 3
                || tile.RowSpan is < 1 or > 3
                || tile.ColumnSpan > columns)
            {
                throw new ArgumentOutOfRangeException(nameof(tiles), "Tile spans must be between one and three cells.");
            }

            var placed = false;
            for (var row = 0; !placed; row++)
            {
                for (var column = 0; column <= columns - tile.ColumnSpan; column++)
                {
                    var isAvailable = true;
                    for (var rowOffset = 0; rowOffset < tile.RowSpan && isAvailable; rowOffset++)
                    {
                        for (var columnOffset = 0; columnOffset < tile.ColumnSpan; columnOffset++)
                        {
                            if (occupied.Contains((column + columnOffset, row + rowOffset)))
                            {
                                isAvailable = false;
                                break;
                            }
                        }
                    }

                    if (!isAvailable)
                    {
                        continue;
                    }

                    for (var rowOffset = 0; rowOffset < tile.RowSpan; rowOffset++)
                    {
                        for (var columnOffset = 0; columnOffset < tile.ColumnSpan; columnOffset++)
                        {
                            occupied.Add((column + columnOffset, row + rowOffset));
                        }
                    }

                    positions.Add(new HomeTileGridPosition(
                        tile.Id,
                        column,
                        row,
                        tile.ColumnSpan,
                        tile.RowSpan));
                    placed = true;
                    break;
                }
            }
        }

        return positions;
    }
}
