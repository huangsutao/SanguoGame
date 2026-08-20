namespace SanguoGame.Core.World;

public sealed class WorldMapOptions
{
    public const string SectionName = "WorldMap";

    public int Width { get; set; } = 200;

    public int Height { get; set; } = 200;

    public int PlacementMaxAttempts { get; set; } = 64;

    public int OutpostCount { get; set; } = 24;

    public int MarketCount { get; set; } = 8;

    public int AiCityCount { get; set; } = 8;

    public int SecondsPerTile { get; set; } = 20;

    public int MinMarchSeconds { get; set; } = 30;

    public int MaxMarchesPerCity { get; set; } = 3;

    public int MaxTransportsPerCity { get; set; } = 3;

    public int ProtectionSeconds { get; set; } = 7200;

    public int OutpostRecoverSeconds { get; set; } = 7200;

    public int AiTickMinutes { get; set; } = 5;
}
