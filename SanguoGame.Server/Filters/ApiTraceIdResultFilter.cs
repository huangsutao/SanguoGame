using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Filters;

/// <summary>
/// 给控制器返回的信封补上当前请求的 TraceId。
/// </summary>
public sealed class ApiTraceIdResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { Value: ApiResultBase envelope } &&
            string.IsNullOrEmpty(envelope.TraceId))
        {
            envelope.TraceId = ApiTrace.GetTraceId(context.HttpContext);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
