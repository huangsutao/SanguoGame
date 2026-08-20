namespace SanguoGame.Core.Social;

public enum MailType
{
    System,
    Battle,
    Alliance,
    Scout
}

public enum AllianceRole
{
    Leader,
    Officer,
    Member
}

public enum AllianceRequestStatus
{
    Pending,
    Accepted,
    Declined
}

public enum RankingType
{
    Power,
    Troops,
    Loot
}

public static class AllianceRules
{
    public const int MaxMembers = 20;
    public const int NameMinLength = 2;
    public const int NameMaxLength = 12;
    public const int NoticeMaxLength = 200;
}

public static class RankingRules
{
    public const int TopSize = 50;

    public static int PowerScore(int buildingLevels, int stationedTroops) =>
        buildingLevels * 100 + stationedTroops;
}
