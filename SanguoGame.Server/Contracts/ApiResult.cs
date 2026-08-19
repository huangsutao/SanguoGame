namespace SanguoGame.Server.Contracts;

public abstract record ApiResultBase
{
    public int Code { get; init; }

    public string Message { get; init; } = "ok";

    public string? TraceId { get; set; }
}

public sealed record ApiResult<T> : ApiResultBase
{
    public T? Data { get; init; }
}

public static class ApiResult
{
    public static ApiResult<T> Ok<T>(T data, string message = "ok") =>
        new()
        {
            Code = 0,
            Message = message,
            Data = data
        };

    public static ApiResult<T> Fail<T>(int code, string message) =>
        new()
        {
            Code = code,
            Message = message,
            Data = default
        };

    public static ApiResult<object?> Fail(int code, string message) =>
        Fail<object?>(code, message);
}
