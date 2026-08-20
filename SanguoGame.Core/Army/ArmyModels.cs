namespace SanguoGame.Core.Army;

public sealed record TroopCount(int Infantry, int Archer, int Cavalry)
{
    public static TroopCount Zero { get; } = new(0, 0, 0);

    public int Total => Infantry + Archer + Cavalry;

    public TroopCount Add(TroopCount other) =>
        new(Infantry + other.Infantry, Archer + other.Archer, Cavalry + other.Cavalry);

    public TroopCount Subtract(TroopCount other) =>
        new(
            Math.Max(0, Infantry - other.Infantry),
            Math.Max(0, Archer - other.Archer),
            Math.Max(0, Cavalry - other.Cavalry));

    public bool CanAfford(TroopCount need) =>
        Infantry >= need.Infantry && Archer >= need.Archer && Cavalry >= need.Cavalry;

    public TroopCount RemainingAfterLoss(double lossRate)
    {
        var keep = Math.Clamp(1d - lossRate, 0d, 1d);
        return new(
            (int)Math.Floor(Infantry * keep),
            (int)Math.Floor(Archer * keep),
            (int)Math.Floor(Cavalry * keep));
    }

    public int Get(string troopType) => troopType switch
    {
        "infantry" => Infantry,
        "archer" => Archer,
        "cavalry" => Cavalry,
        _ => throw new ArgumentOutOfRangeException(nameof(troopType), troopType, "未知兵种")
    };

    public TroopCount Add(string troopType, int delta) => troopType switch
    {
        "infantry" => this with { Infantry = Infantry + delta },
        "archer" => this with { Archer = Archer + delta },
        "cavalry" => this with { Cavalry = Cavalry + delta },
        _ => throw new ArgumentOutOfRangeException(nameof(troopType), troopType, "未知兵种")
    };
}

public sealed record TroopDef(
    string Type,
    string Name,
    int RequireBarracksLevel,
    SanguoGame.Core.Buildings.ResourceAmount UnitCost);

public static class TroopCatalog
{
    public static IReadOnlyList<TroopDef> All { get; } =
    [
        new("infantry", "步兵", 1, new SanguoGame.Core.Buildings.ResourceAmount(20, 5, 10, 0)),
        new("archer", "弓兵", 2, new SanguoGame.Core.Buildings.ResourceAmount(10, 20, 8, 5)),
        new("cavalry", "骑兵", 3, new SanguoGame.Core.Buildings.ResourceAmount(15, 10, 20, 5))
    ];

    public static TroopDef? Find(string troopType) =>
        All.FirstOrDefault(def => def.Type.Equals(troopType, StringComparison.OrdinalIgnoreCase));

    public static SanguoGame.Core.Buildings.ResourceAmount Cost(TroopDef def, int count)
    {
        if (count <= 0)
        {
            return SanguoGame.Core.Buildings.ResourceAmount.Zero;
        }

        return new SanguoGame.Core.Buildings.ResourceAmount(
            def.UnitCost.Grain * count,
            def.UnitCost.Wood * count,
            def.UnitCost.Iron * count,
            def.UnitCost.Copper * count);
    }
}

public enum MarchTargetType
{
    Outpost = 0,
    City = 1
}

public enum MarchStatus
{
    Marching = 0,
    Settled = 1
}

public enum MarchKind
{
    Attack = 0,
    Scout = 1
}

public sealed record BattleInput(
    TroopCount Attacker,
    TroopCount Defender,
    int AcademyLevel,
    int WallDefense,
    int OutpostBasePower,
    double TrapBonus,
    int Seed,
    int AttackerPowerPercent = 0,
    int DefenderPowerPercent = 0);

public sealed record BattleOutcome(
    bool AttackerWon,
    TroopCount AttackerBefore,
    TroopCount AttackerAfter,
    TroopCount DefenderBefore,
    TroopCount DefenderAfter,
    int Seed);

public static class BattleCalculator
{
    public static int Power(TroopCount troops) =>
        troops.Infantry * 10 + troops.Archer * 12 + troops.Cavalry * 14;

    public static BattleOutcome Resolve(BattleInput input)
    {
        var rng = new Random(input.Seed);
        var atkPower = ApplyPowerPercent(Power(input.Attacker), input.AttackerPowerPercent);
        var defPower = ApplyPowerPercent(Power(input.Defender), input.DefenderPowerPercent);
        var atk = atkPower * (100 + Math.Max(0, input.AcademyLevel) * 2) / 100d;
        var def = defPower + input.WallDefense * 10 + Math.Max(0, input.OutpostBasePower);
        var atkRoll = atk * (90 + rng.Next(0, 21)) / 100d;
        var defRoll = def * (90 + rng.Next(0, 21)) / 100d;
        var won = atkRoll >= defRoll;

        var atkLoss = won
            ? 0.15 + rng.NextDouble() * 0.15
            : 0.55 + rng.NextDouble() * 0.25;
        var defLoss = won
            ? 0.55 + rng.NextDouble() * 0.25
            : 0.15 + rng.NextDouble() * 0.15;
        atkLoss = Math.Clamp(atkLoss + Math.Max(0, input.TrapBonus), 0, 0.95);

        return new BattleOutcome(
            won,
            input.Attacker,
            input.Attacker.RemainingAfterLoss(atkLoss),
            input.Defender,
            input.Defender.RemainingAfterLoss(defLoss),
            input.Seed);
    }

    private static int ApplyPowerPercent(int power, int percent) =>
        SanguoGame.Core.Buildings.TechBonuses.ApplyPercent(power, percent);
}

public static class MarchTiming
{
    public static int DurationSeconds(int fromX, int fromY, int toX, int toY, int secondsPerTile, int minSeconds)
    {
        var distance = Math.Abs(fromX - toX) + Math.Abs(fromY - toY);
        return Math.Max(minSeconds, distance * secondsPerTile);
    }

    public static int ScoutDurationSeconds(int fromX, int fromY, int toX, int toY, int secondsPerTile, int minSeconds)
    {
        var distance = Math.Abs(fromX - toX) + Math.Abs(fromY - toY);
        var halfMin = (minSeconds + 1) / 2;
        return Math.Max(halfMin, distance * secondsPerTile / 2);
    }
}
