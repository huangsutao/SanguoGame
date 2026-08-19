namespace SanguoGame.Core;

/// <summary>
/// 可预期的业务失败。由 API 异常过滤器转成 HTTP 200 + 信封，<see cref="Code"/> 写入 <c>code</c>。
/// </summary>
public sealed class BizException : Exception
{
    public int Code { get; }

    public BizException(int code, string message)
        : base(message)
    {
        Code = code;
    }
}
