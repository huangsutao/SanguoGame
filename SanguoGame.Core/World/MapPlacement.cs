namespace SanguoGame.Core.World;

/// <summary>
/// 在逻辑地图上均匀随机挑一格空地。占用判定由调用方提供（查库或内存）。
/// </summary>
public static class MapPlacement
{
    public static bool TryPickEmptyCell(
        int width,
        int height,
        int maxAttempts,
        Func<int, int, bool> isOccupied,
        out int x,
        out int y,
        Random? random = null)
    {
        x = 0;
        y = 0;
        if (width <= 0 || height <= 0 || maxAttempts <= 0)
        {
            return false;
        }

        random ??= Random.Shared;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            x = random.Next(width);
            y = random.Next(height);
            if (!isOccupied(x, y))
            {
                return true;
            }
        }

        return false;
    }
}
