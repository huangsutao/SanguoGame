using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SanguoGame.Core;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Filters;

/// <summary>
/// 接管 [ApiController] 的默认 400 ProblemDetails，改为统一信封。
/// </summary>
public sealed class ApiValidationFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
        {
            return;
        }

        var errors = context.ModelState
            .Where(pair => pair.Value is { Errors.Count: > 0 })
            .SelectMany(pair => pair.Value!.Errors.Select(error =>
            {
                if (!string.IsNullOrWhiteSpace(error.ErrorMessage))
                {
                    return error.ErrorMessage;
                }

                return error.Exception?.Message;
            }))
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message!);

        var message = string.Join("; ", errors);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = "参数校验失败";
        }

        var result = ApiResult.Fail(ErrorCodes.ValidationFailed, message);
        result.TraceId = ApiTrace.GetTraceId(context.HttpContext);

        context.Result = new ObjectResult(result)
        {
            StatusCode = StatusCodes.Status200OK
        };
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
