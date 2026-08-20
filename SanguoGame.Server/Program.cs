using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SanguoGame.Core;
using SanguoGame.Infrastructure;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Filters;
using SanguoGame.Server.Hubs;
using SanguoGame.Server.Jobs;
using SanguoGame.Server.Json;
using SanguoGame.Server.Options;
using SanguoGame.Server.Security;

namespace SanguoGame.Server;

public class Program
{
    public static void Main(string[] args)
    {
        // Npgsql 6+ 拒绝把 Kind=UTC 写入 timestamp without time zone。
        // FreeSql 建表用的是无时区 timestamp，需恢复旧行为。
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var builder = WebApplication.CreateBuilder(args);
        var testing = builder.Configuration.GetValue("Testing:DisableBackgroundJobs", false);

        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddGameAuth(builder.Configuration, builder.Environment);
        if (testing)
        {
            builder.Services.AddSingleton<IBackgroundJobClient, NoopBackgroundJobClient>();
        }
        else
        {
            builder.Services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(
                    options => options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Default")),
                    new PostgreSqlStorageOptions
                    {
                        SchemaName = "hangfire",
                        PrepareSchemaIfNecessary = true
                    }));
            builder.Services.AddHangfireServer();
            builder.Services.AddHostedService<GameBootHostedService>();
        }
        builder.Services.AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                options.PayloadSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
            });
        builder.Services.AddOpenApi();
        if (!testing)
        {
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("auth", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 20,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));
                options.OnRejected = async (context, cancellationToken) =>
                {
                    var http = context.HttpContext;
                    if (http.Response.HasStarted)
                    {
                        return;
                    }

                    http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    var envelope = ApiResult.Fail(ErrorCodes.TooManyRequests, "请求过于频繁");
                    envelope.TraceId = ApiTrace.GetTraceId(http);
                    var jsonOptions = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
                    await http.Response.WriteAsJsonAsync(envelope, jsonOptions, cancellationToken);
                };
            });
        }

        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
            ?? ["http://localhost:5173"];
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("web", policy =>
                policy.WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
        });

        builder.Services.AddControllers(options =>
            {
                options.Filters.Add<ApiExceptionFilter>();
                options.Filters.Add<ApiValidationFilter>();
                options.Filters.Add<ApiTraceIdResultFilter>();
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
            });

        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.SuppressModelStateInvalidFilter = true;
        });

        var app = builder.Build();
        EnsureProductionSecrets(app);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
        else
        {
            app.UseHttpsRedirection();
        }
        app.UseCors("web");
        if (!testing)
        {
            app.UseRateLimiter();
        }
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<GameHub>("/hubs/game");

        if (!testing)
        {
            var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
            var aiTickMinutes = Math.Max(1, app.Configuration.GetValue("WorldMap:AiTickMinutes", 5));
            recurringJobs.AddOrUpdate<AiTickJob>(
                "ai-tick",
                job => job.Execute(),
                $"*/{aiTickMinutes} * * * *");
            var roamingTickMinutes = Math.Max(1, app.Configuration.GetValue("WorldMap:RoamingOutpostTickMinutes", 1));
            recurringJobs.AddOrUpdate<RoamingOutpostJob>(
                "roaming-outpost-tick",
                job => job.Execute(),
                $"*/{roamingTickMinutes} * * * *");
        }

        app.Run();
    }

    private static void EnsureProductionSecrets(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            return;
        }

        if (app.Configuration.GetValue("FreeSql:AutoSyncStructure", false))
        {
            throw new InvalidOperationException("生产环境禁止 FreeSql:AutoSyncStructure");
        }

        var signingKey = app.Configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey) ||
            string.Equals(signingKey, JwtOptions.DevelopmentSigningKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("生产环境必须通过环境变量配置独立的 Jwt:SigningKey");
        }
    }
}
