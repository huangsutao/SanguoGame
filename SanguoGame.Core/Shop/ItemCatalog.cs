using SanguoGame.Core.Buildings;

namespace SanguoGame.Core.Shop;

public enum ItemKind
{
    Buff = 0,
    Consumable = 1,
    Unlock = 2
}

public sealed record ItemDef(
    string Type,
    string Name,
    ItemKind Kind,
    int Price,
    int DurationHours,
    int SpeedPercent,
    string Description);

public sealed record ActiveBuff(
    string Type,
    DateTime ExpireAt);

public static class ItemCatalog
{
    public const string SpeedBuild = "speedBuild";
    public const string SpeedUpgrade = "speedUpgrade";
    public const string SpeedTech = "speedTech";
    public const string SpeedRecruit = "speedRecruit";
    public const string ResourceBoost = "resourceBoost";
    public const string RelocateRandom = "relocateRandom";
    public const string RelocateTarget = "relocateTarget";
    public const string QueueBuild = "queueBuild";
    public const string QueueField = "queueField";
    public const string QueueTech = "queueTech";
    public const string QueueRecruit = "queueRecruit";

    public const int SpeedPercent = 50;
    public const int DurationHours = 5;
    public const int MaxStackDays = 30;
    public const int MaxBuyCount = 99;
    public const int ResourceBoostPercent = 50;

    public static IReadOnlyList<ItemDef> All { get; } =
    [
        new(
            SpeedBuild,
            "建造加速令",
            ItemKind.Buff,
            80,
            DurationHours,
            SpeedPercent,
            "主殿、民居、仓库、兵营建造与升级加速 50%，持续 5 小时，重复使用时间累加。"),
        new(
            SpeedUpgrade,
            "升级加速令",
            ItemKind.Buff,
            80,
            DurationHours,
            SpeedPercent,
            "城外田与城墙建造与升级加速 50%，持续 5 小时，重复使用时间累加。"),
        new(
            SpeedTech,
            "研发加速令",
            ItemKind.Buff,
            100,
            DurationHours,
            SpeedPercent,
            "书院、演武堂、城防署、司农院升级加速 50%，持续 5 小时，重复使用时间累加。"),
        new(
            SpeedRecruit,
            "征兵加速令",
            ItemKind.Buff,
            80,
            DurationHours,
            SpeedPercent,
            "征兵加速 50%，持续 5 小时，重复使用时间累加。"),
        new(
            ResourceBoost,
            "丰收令",
            ItemKind.Buff,
            120,
            DurationHours,
            ResourceBoostPercent,
            "田产出速率 +50%，持续 5 小时，重复使用时间累加。可与司农院加算。"),
        new(
            RelocateRandom,
            "迁城令",
            ItemKind.Consumable,
            150,
            0,
            0,
            "迁到地图随机空地，并进入保护。"),
        new(
            RelocateTarget,
            "高级迁城令",
            ItemKind.Consumable,
            400,
            0,
            0,
            "迁到指定空地坐标，并进入保护。"),
        new(
            QueueBuild,
            "建造队列令",
            ItemKind.Unlock,
            200,
            0,
            0,
            "使用后永久额外增加 1 条建造队列（主殿、民居、仓库、兵营、城墙）。每城限用 1 张。"),
        new(
            QueueField,
            "资源队列令",
            ItemKind.Unlock,
            200,
            0,
            0,
            "使用后永久额外增加 1 条资源田建造队列。每城限用 1 张。"),
        new(
            QueueTech,
            "科技队列令",
            ItemKind.Unlock,
            200,
            0,
            0,
            "使用后永久额外增加 1 条科技建筑建造队列。每城限用 1 张。"),
        new(
            QueueRecruit,
            "征兵队列令",
            ItemKind.Unlock,
            200,
            0,
            0,
            "使用后永久额外增加 1 条征兵队列。每城限用 1 张。")
    ];

    public static ItemDef? Find(string itemType) =>
        All.FirstOrDefault(def => def.Type.Equals(itemType, StringComparison.OrdinalIgnoreCase));

    public static bool IsBuff(string itemType) =>
        Find(itemType) is { Kind: ItemKind.Buff };

    public static bool IsRelocate(string itemType) =>
        itemType.Equals(RelocateRandom, StringComparison.OrdinalIgnoreCase)
        || itemType.Equals(RelocateTarget, StringComparison.OrdinalIgnoreCase);

    public static QueueKind? QueueKindOf(string itemType) => itemType switch
    {
        _ when itemType.Equals(QueueBuild, StringComparison.OrdinalIgnoreCase) => QueueKind.Build,
        _ when itemType.Equals(QueueField, StringComparison.OrdinalIgnoreCase) => QueueKind.Field,
        _ when itemType.Equals(QueueTech, StringComparison.OrdinalIgnoreCase) => QueueKind.Tech,
        _ when itemType.Equals(QueueRecruit, StringComparison.OrdinalIgnoreCase) => QueueKind.Recruit,
        _ => null
    };

    public static bool IsQueueUnlock(string itemType) => QueueKindOf(itemType) is not null;

    public static string? SpeedKindOf(string buildingType) => buildingType switch
    {
        "palace" or "house" or "warehouse" or "barracks" => SpeedBuild,
        "academy" or "drillHall" or "defenseHall" or "resourceHall" => SpeedTech,
        "farm" or "lumber" or "ironMine" or "copperMine" or "arrowTower" or "gate" or "trap" => SpeedUpgrade,
        _ => null
    };

    public static int SpeedPercentOf(string buildingType, IReadOnlyList<ActiveBuff> buffs, DateTime now)
    {
        var kind = SpeedKindOf(buildingType);
        return kind is null ? 0 : ActivePercent(buffs, kind, now);
    }

    public static int RecruitSpeedPercent(IReadOnlyList<ActiveBuff> buffs, DateTime now) =>
        ActivePercent(buffs, SpeedRecruit, now);

    public static int ResourceBoostOf(IReadOnlyList<ActiveBuff> buffs, DateTime now) =>
        ActivePercent(buffs, ResourceBoost, now);

    public static DateTime? ResourceBoostExpireAt(IReadOnlyList<ActiveBuff> buffs, DateTime now)
    {
        var buff = buffs.FirstOrDefault(b =>
            b.Type.Equals(ResourceBoost, StringComparison.OrdinalIgnoreCase) && b.ExpireAt > now);
        return buff?.ExpireAt;
    }

    public static int ActivePercent(IReadOnlyList<ActiveBuff> buffs, string type, DateTime now)
    {
        var def = Find(type);
        if (def is null || def.Kind != ItemKind.Buff)
        {
            return 0;
        }

        return buffs.Any(b => b.Type.Equals(type, StringComparison.OrdinalIgnoreCase) && b.ExpireAt > now)
            ? def.SpeedPercent
            : 0;
    }

    public static int ApplySpeed(int baseSeconds, int speedPercent)
    {
        if (baseSeconds <= 0)
        {
            return 0;
        }

        if (speedPercent <= 0)
        {
            return baseSeconds;
        }

        return Math.Max(1, (int)Math.Ceiling(baseSeconds * 100d / (100d + speedPercent)));
    }

    public static DateTime ShortenRemaining(DateTime finishAt, DateTime now, int oldPercent, int newPercent)
    {
        now = AsUtc(now);
        finishAt = AsUtc(finishAt);
        if (finishAt <= now || newPercent <= oldPercent)
        {
            return finishAt;
        }

        var remaining = (finishAt - now).TotalSeconds;
        var unbuffed = remaining * (100d + Math.Max(0, oldPercent)) / 100d;
        var next = unbuffed * 100d / (100d + newPercent);
        return now.AddSeconds(Math.Max(1, Math.Ceiling(next)));
    }

    public static DateTime StackExpireAt(DateTime now, DateTime? currentExpireAt, int durationHours, int count)
    {
        now = AsUtc(now);
        var hours = Math.Max(0, durationHours) * Math.Max(1, count);
        var start = currentExpireAt is { } until && AsUtc(until) > now ? AsUtc(until) : now;
        var expire = start.AddHours(hours);
        var cap = now.AddDays(MaxStackDays);
        return expire > cap ? cap : expire;
    }

    public static bool TryBuyCost(int price, int count, out int total)
    {
        total = 0;
        if (price < 0 || count is < 1 or > MaxBuyCount)
        {
            return false;
        }

        var raw = (long)price * count;
        if (raw > int.MaxValue)
        {
            return false;
        }

        total = (int)raw;
        return true;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
