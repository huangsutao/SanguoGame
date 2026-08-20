namespace SanguoGame.Server.Services;

internal static class UtcSchedule
{
    public static DateTimeOffset At(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTimeOffset(utc);
    }
}
