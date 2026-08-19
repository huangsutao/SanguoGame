using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SanguoGame.Core;
using SanguoGame.Core.World;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Filters;
using SanguoGame.Server.Options;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Security;

internal static class AuthenticationExtensions
{
    public static IServiceCollection AddGameAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.SigningKey) && options.SigningKey.Length >= 32,
                "Jwt:SigningKey 至少 32 字符")
            .Validate(options => options.AccessTokenMinutes > 0, "Jwt:AccessTokenMinutes 必须为正")
            .Validate(options => options.RefreshTokenDays > 0, "Jwt:RefreshTokenDays 必须为正")
            .ValidateOnStart();

        services.AddOptions<WorldMapOptions>()
            .Bind(configuration.GetSection(WorldMapOptions.SectionName))
            .Validate(options => options.Width >= 1 && options.Height >= 1, "WorldMap 宽高必须为正")
            .Validate(options => options.PlacementMaxAttempts >= 1, "WorldMap:PlacementMaxAttempts 必须为正")
            .ValidateOnStart();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("缺少 Jwt 配置");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                    NameClaimType = JwtRegisteredClaimNames.Sub
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) &&
                            context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await WriteEnvelopeAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            ErrorCodes.Unauthorized,
                            "未登录或令牌无效");
                    },
                    OnForbidden = async context =>
                    {
                        await WriteEnvelopeAsync(
                            context.HttpContext,
                            StatusCodes.Status403Forbidden,
                            ErrorCodes.Forbidden,
                            "无权操作");
                    }
                };
            });

        services.AddAuthorization();
        services.AddSingleton<JwtIssuer>();
        services.AddSingleton<PasswordHasher<AccountEntity>>();
        services.AddScoped<AuthService>();
        services.AddScoped<CharacterService>();
        services.AddScoped<CityService>();
        services.AddScoped<BuildingService>();
        return services;
    }

    public static long GetAccountId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");
        if (!long.TryParse(value, out var accountId))
        {
            throw new BizException(ErrorCodes.Unauthorized, "未登录或令牌无效");
        }

        return accountId;
    }

    private static async Task WriteEnvelopeAsync(HttpContext http, int status, int code, string message)
    {
        if (http.Response.HasStarted)
        {
            return;
        }

        http.Response.StatusCode = status;
        var envelope = ApiResult.Fail(code, message);
        envelope.TraceId = ApiTrace.GetTraceId(http);

        var jsonOptions = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        await http.Response.WriteAsJsonAsync(envelope, jsonOptions);
    }
}
