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

    /// <summary>同联盟不可交战。</summary>
    public const int SameAlliance = 40918;

    /// <summary>已加入联盟。</summary>
    public const int AlreadyInAlliance = 40919;

    /// <summary>联盟名已被占用。</summary>
    public const int AllianceNameTaken = 40920;

    /// <summary>联盟人数已满。</summary>
    public const int AllianceFull = 40921;

    /// <summary>未加入联盟。</summary>
    public const int NotInAlliance = 40922;

    /// <summary>联盟权限不足。</summary>
    public const int AlliancePermission = 40923;

    /// <summary>邀请或申请已失效。</summary>
    public const int AllianceInviteInvalid = 40924;

    /// <summary>不能运给自己。</summary>
    public const int CannotAidSelf = 40925;

    /// <summary>非同联盟不可运输。</summary>
    public const int NotAlliedTransport = 40926;

    /// <summary>运输数量已达上限。</summary>
    public const int TransportLimit = 40927;

    /// <summary>运量超限或兑换数量非法。</summary>
    public const int InvalidTrade = 40928;

    /// <summary>军务未完成或已领取。</summary>
    public const int DailyNotClaimable = 40929;

    /// <summary>目标不可侦察。</summary>
    public const int ScoutNotAllowed = 40930;

    /// <summary>请求过于频繁。</summary>
    public const int TooManyRequests = 42900;

    /// <summary>未处理的服务器异常。</summary>
    public const int InternalError = 50000;
}
