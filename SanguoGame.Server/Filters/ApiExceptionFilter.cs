using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SanguoGame.Core;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Filters;

public sealed class ApiExceptionFilter : IExceptionFilter
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ApiExceptionFilter> _logger;

    public ApiExceptionFilter(IHostEnvironment environment, ILogger<ApiExceptionFilter> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        var traceId = ApiTrace.GetTraceId(context.HttpContext);

        if (context.Exception is BizException biz)
        {
            context.Result = CreateEnvelope(biz.Code, biz.Message, StatusCodes.Status200OK, traceId);
            context.ExceptionHandled = true;
            return;
        }

        _logger.LogError(context.Exception, "未处理异常, TraceId={TraceId}", traceId);

        var message = _environment.IsDevelopment()
            ? context.Exception.Message
            : "服务器内部错误";

        context.Result = CreateEnvelope(ErrorCodes.InternalError, message, StatusCodes.Status500InternalServerError, traceId);
        context.ExceptionHandled = true;
    }

    private static ObjectResult CreateEnvelope(int code, string message, int statusCode, string traceId) =>
        new(ApiResult.Fail(code, message) with { TraceId = traceId })
        {
            StatusCode = statusCode
        };
}
