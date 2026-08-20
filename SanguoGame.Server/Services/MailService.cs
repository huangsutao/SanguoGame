using System.Data.Common;
using FreeSql;
using SanguoGame.Core;
using SanguoGame.Core.Social;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Services;

public sealed class MailService
{
    private readonly IFreeSql _orm;

    public MailService(IFreeSql orm)
    {
        _orm = orm;
    }

    public async Task<MailListDto> ListAsync(long accountId, PagedQuery query, bool unreadOnly, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        var filter = _orm.Select<MailEntity>().Where(m => m.RecipientCharacterId == character.Id);
        if (unreadOnly)
        {
            filter = filter.Where(m => !m.IsRead);
        }

        var unreadCount = (int)await _orm.Select<MailEntity>()
            .Where(m => m.RecipientCharacterId == character.Id && !m.IsRead)
            .CountAsync(cancellationToken);
        var total = (int)await filter.CountAsync(cancellationToken);
        var rows = await filter
            .OrderByDescending(m => m.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new MailListDto(
            unreadCount,
            rows.Select(Map).ToList(),
            query.Page,
            query.PageSize,
            total);
    }

    public async Task ReadAsync(long accountId, long mailId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        var mail = await _orm.Select<MailEntity>()
            .Where(m => m.Id == mailId && m.RecipientCharacterId == character.Id)
            .FirstAsync(cancellationToken);
        if (mail is null)
        {
            throw new BizException(ErrorCodes.NotFound, "邮件不存在");
        }

        if (mail.IsRead)
        {
            return;
        }

        mail.IsRead = true;
        await _orm.Update<MailEntity>()
            .SetSource(mail)
            .UpdateColumns(m => m.IsRead)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    public async Task ReadAllAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        await _orm.Update<MailEntity>()
            .Where(m => m.RecipientCharacterId == character.Id && !m.IsRead)
            .Set(m => m.IsRead, true)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    public Task SendAsync(
        long recipientCharacterId,
        MailType type,
        string title,
        string body,
        string? relatedType,
        long? relatedId,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null) =>
        SendManyAsync(
            [recipientCharacterId],
            type,
            title,
            body,
            relatedType,
            relatedId,
            cancellationToken,
            transaction);

    public async Task SendManyAsync(
        IReadOnlyCollection<long> recipientCharacterIds,
        MailType type,
        string title,
        string body,
        string? relatedType,
        long? relatedId,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null)
    {
        if (recipientCharacterIds.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var rows = recipientCharacterIds
            .Distinct()
            .Select(id => new MailEntity
            {
                RecipientCharacterId = id,
                Type = type,
                Title = title,
                Body = body,
                RelatedType = relatedType,
                RelatedId = relatedId,
                CreatedAt = now
            })
            .ToList();
        var insert = _orm.Insert(rows);
        if (transaction is not null)
        {
            insert = insert.WithTransaction(transaction);
        }

        await insert.ExecuteAffrowsAsync(cancellationToken);
    }

    private async Task<CharacterEntity> RequireCharacterAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await _orm.Select<CharacterEntity>()
            .Where(c => c.AccountId == accountId)
            .FirstAsync(cancellationToken);
        if (character is null)
        {
            throw new BizException(ErrorCodes.NotFound, "尚未创建角色");
        }

        return character;
    }

    private static MailDto Map(MailEntity row) =>
        new(row.Id, row.Type, row.Title, row.Body, row.RelatedType, row.RelatedId, row.IsRead, row.CreatedAt);
}
