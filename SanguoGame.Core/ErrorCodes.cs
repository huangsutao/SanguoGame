namespace SanguoGame.Core;

/// <summary>
/// HTTP 信封中的业务错误码。0 表示成功，其余分段预留给校验、鉴权与业务冲突。
/// </summary>
public static class ErrorCodes
{
    public const int Success = 0;

    /// <summary>请求参数校验失败。</summary>
    public const int ValidationFailed = 40001;

    /// <summary>未登录或令牌无效（鉴权接入后使用）。</summary>
    public const int Unauthorized = 40100;

    /// <summary>已登录但无权操作（鉴权接入后使用）。</summary>
    public const int Forbidden = 40300;

    /// <summary>资源不存在。</summary>
    public const int NotFound = 40400;

    /// <summary>业务冲突，如坐标占用、重复建城。具体场景可在 409xx 继续细分。</summary>
    public const int Conflict = 40900;

    /// <summary>未处理的服务器异常。</summary>
    public const int InternalError = 50000;
}
