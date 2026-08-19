using System.Diagnostics;

namespace SanguoGame.Server.Filters;

internal static class ApiTrace
{
    public static string GetTraceId(HttpContext httpContext) =>
        Activity.Current?.Id ?? httpContext.TraceIdentifier;
}
