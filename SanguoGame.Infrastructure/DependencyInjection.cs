using Microsoft.Extensions.DependencyInjection;

namespace SanguoGame.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// 注册基础设施。FreeSql / Redis / Hangfire 后续在此接入。
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        return services;
    }
}
