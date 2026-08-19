using System.ComponentModel.DataAnnotations;

namespace SanguoGame.Server.Contracts;

/// <summary>
/// 列表查询的统一分页参数。页码从 1 开始。
/// </summary>
public sealed class PagedQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int Total { get; init; }
}
