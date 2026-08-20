namespace SanguoGame.Core.Shop;

/// <summary>
/// 出征战斗结束后给攻方掷元宝。与战报 seed 派生，保证同一场战斗结果可复现。
/// </summary>
public static class YuanbaoLoot
{
    public const int WinChancePercent = 70;
    public const int LoseChancePercent = 30;
    public const int WinMin = 20;
    public const int WinMax = 40;
    public const int LoseMin = 5;
    public const int LoseMax = 12;

    public static int Roll(int seed, bool attackerWon)
    {
        var rng = new Random(unchecked(seed ^ 0x5EEDB0A));
        var chance = attackerWon ? WinChancePercent : LoseChancePercent;
        if (rng.Next(100) >= chance)
        {
            return 0;
        }

        var min = attackerWon ? WinMin : LoseMin;
        var max = attackerWon ? WinMax : LoseMax;
        return rng.Next(min, max + 1);
    }

    public static int Grant(int current, int gained)
    {
        if (gained <= 0)
        {
            return Math.Max(0, current);
        }

        return (int)Math.Min(int.MaxValue, (long)Math.Max(0, current) + gained);
    }
}
