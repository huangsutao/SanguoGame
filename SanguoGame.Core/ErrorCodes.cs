namespace SanguoGame.Core;

/// <summary>
/// HTTP 信封中的业务错误码。0 表示成功，其余分段预留给校验、鉴权与业务冲突。
/// </summary>
public static class ErrorCodes
{
    public const int Success = 0;

    /// <summary>请求参数校验失败。</summary>
    public const int ValidationFailed = 40001;

    /// <summary>未登录、令牌无效，或登录失败。</summary>
    public const int Unauthorized = 40100;

    /// <summary>已登录但无权操作。</summary>
    public const int Forbidden = 40300;

    /// <summary>资源不存在（无角色、无城等）。</summary>
    public const int NotFound = 40400;

    /// <summary>业务冲突（未再细分时的兜底）。</summary>
    public const int Conflict = 40900;

    /// <summary>用户名已注册。</summary>
    public const int UsernameTaken = 40901;

    /// <summary>该账号已有角色。</summary>
    public const int CharacterExists = 40902;

    /// <summary>角色名已被占用。</summary>
    public const int CharacterNameTaken = 40903;

    /// <summary>该角色已有主城。</summary>
    public const int CityExists = 40904;

    /// <summary>无空地可建城。</summary>
    public const int MapFull = 40905;

    /// <summary>资源不足。</summary>
    public const int InsufficientResources = 40906;

    /// <summary>本城建造队列占用中。</summary>
    public const int BuildingQueueBusy = 40907;

    /// <summary>建筑已满级。</summary>
    public const int BuildingMaxLevel = 40908;

    /// <summary>建筑前置未满足。</summary>
    public const int BuildingPrerequisite = 40909;

    /// <summary>兵力不足。</summary>
    public const int InsufficientTroops = 40910;

    /// <summary>目标处于保护期。</summary>
    public const int CityProtected = 40912;

    /// <summary>不能进攻自己的城。</summary>
    public const int CannotAttackSelf = 40914;

    /// <summary>兵营等级不足。</summary>
    public const int BarracksRequired = 40915;

    /// <summary>超出带兵上限。</summary>
    public const int TroopCapExceeded = 40916;

    /// <summary>行军数量已达上限。</summary>
    public const int MarchLimit = 40917;

    /// <summary>请求过于频繁。</summary>
    public const int TooManyRequests = 42900;

    /// <summary>未处理的服务器异常。</summary>
    public const int InternalError = 50000;
}
