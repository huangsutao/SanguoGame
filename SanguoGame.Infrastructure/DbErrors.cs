using Npgsql;

namespace SanguoGame.Infrastructure;

public static class DbErrors
{
    public static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return true;
            }
        }

        return false;
    }
}
