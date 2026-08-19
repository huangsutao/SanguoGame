namespace SanguoGame.Core.World;

public sealed class WorldMapOptions
{
    public const string SectionName = "WorldMap";

    public int Width { get; set; } = 200;

    public int Height { get; set; } = 200;

    public int PlacementMaxAttempts { get; set; } = 64;
}
