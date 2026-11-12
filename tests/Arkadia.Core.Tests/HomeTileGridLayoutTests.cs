using Arkadia.Core.Services;
using Xunit;

namespace Arkadia.Core.Tests;

public sealed class HomeTileGridLayoutTests
{
    [Fact]
    public void ArrangePlacesTilesAtFirstAvailablePositionWithoutOverlappingSpans()
    {
        HomeTileGridItem[] tiles =
        [
            new("wide", 2, 1),
            new("square"),
            new("tall", 1, 2),
            new("large", 2, 2)
        ];

        var result = HomeTileGridLayout.Arrange(tiles, 3);

        Assert.Equal(
            [
                new HomeTileGridPosition("wide", 0, 0, 2, 1),
                new HomeTileGridPosition("square", 2, 0, 1, 1),
                new HomeTileGridPosition("tall", 0, 1, 1, 2),
                new HomeTileGridPosition("large", 1, 1, 2, 2)
            ],
            result);
        AssertNoOverlaps(result);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 4)]
    public void ArrangeRejectsInvalidTileSpans(int columnSpan, int rowSpan)
    {
        HomeTileGridItem[] tiles = [new("invalid", columnSpan, rowSpan)];

        Assert.Throws<ArgumentOutOfRangeException>(() => HomeTileGridLayout.Arrange(tiles, 3));
    }

    [Fact]
    public void ArrangeRejectsDuplicateTileIdentifiers()
    {
        HomeTileGridItem[] tiles = [new("duplicate"), new("duplicate")];

        Assert.Throws<ArgumentException>(() => HomeTileGridLayout.Arrange(tiles, 3));
    }

    private static void AssertNoOverlaps(IReadOnlyList<HomeTileGridPosition> positions)
    {
        for (var firstIndex = 0; firstIndex < positions.Count; firstIndex++)
        {
            var first = positions[firstIndex];
            for (var secondIndex = firstIndex + 1; secondIndex < positions.Count; secondIndex++)
            {
                var second = positions[secondIndex];
                var overlapsColumns = first.Column < second.Column + second.ColumnSpan
                    && second.Column < first.Column + first.ColumnSpan;
                var overlapsRows = first.Row < second.Row + second.RowSpan
                    && second.Row < first.Row + first.RowSpan;

                Assert.False(overlapsColumns && overlapsRows);
            }
        }
    }
}
