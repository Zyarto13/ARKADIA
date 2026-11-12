namespace Arkadia.Core.Models;

public sealed record HomeTileLayoutEntry(
    string Id,
    bool IsVisible = true,
    int ColumnSpan = 1,
    int RowSpan = 1);
