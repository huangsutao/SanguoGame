using System.Data.Common;
using FreeSql;
using SanguoGame.Core;
using SanguoGame.Core.Social;
using SanguoGame.Infrastructure;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Services;

public sealed class AllianceService
{
    private readonly IFreeSql _orm;
    private readonly MailService _mail;

    public AllianceService(IFreeSql orm, MailService mail)
    {
        _orm = orm;
        _mail = mail;
    }

    public async Task<bool> AreAlliedByCityAsync(long cityIdA, long cityIdB, CancellationToken cancellationToken)
    {
        if (cityIdA == cityIdB)
        {
            return false;
        }

        var cities = await _orm.Select<CityEntity>()
            .Where(c => c.Id == cityIdA || c.Id == cityIdB)
            .ToListAsync(cancellationToken);
        if (cities.Count != 2)
        {
            return false;
        }

        var characterIds = cities.Select(c => c.CharacterId).ToArray();
        var members = await _orm.Select<AllianceMemberEntity>()
            .Where(m => characterIds.Contains(m.CharacterId))
            .ToListAsync(cancellationToken);
        return members.Count == 2 && members[0].AllianceId == members[1].AllianceId;
    }

    public async Task<AllianceDetailDto> CreateAsync(long accountId, CreateAllianceRequest request, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterWithCityAsync(accountId, cancellationToken);
        var name = request.Name.Trim();
        ValidateName(name);
        if (await FindMemberAsync(character.Id, cancellationToken) is not null)
        {
            throw new BizException(ErrorCodes.AlreadyInAlliance, "已加入联盟");
        }

        var now = DateTime.UtcNow;
        var alliance = new AllianceEntity
        {
            Name = name,
            NameNormalized = NormalizeName(name),
            LeaderCharacterId = character.Id,
            Notice = "",
            CreatedAt = now
        };

        try
        {
            await InTransactionAsync(async transaction =>
            {
                alliance.Id = await _orm.Insert(alliance).WithTransaction(transaction).ExecuteIdentityAsync(cancellationToken);
                await _orm.Insert(new AllianceMemberEntity
                {
                    AllianceId = alliance.Id,
                    CharacterId = character.Id,
                    Role = AllianceRole.Leader,
                    JoinedAt = now
                }).WithTransaction(transaction).ExecuteAffrowsAsync(cancellationToken);
                await CancelPendingForCharacterAsync(character.Id, cancellationToken, transaction);
                return 0;
            }, cancellationToken);
        }
        catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new BizException(ErrorCodes.AllianceNameTaken, "联盟名已被占用");
        }

        return await GetDetailAsync(alliance.Id, character.Id, cancellationToken);
    }

    public async Task<PagedResult<AllianceSummaryDto>> ListAsync(PagedQuery query, CancellationToken cancellationToken)
    {
        var total = (int)await _orm.Select<AllianceEntity>().CountAsync(cancellationToken);
        var rows = await _orm.Select<AllianceEntity>()
            .OrderBy(a => a.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var ids = rows.Select(a => a.Id).ToArray();
        var counts = ids.Length == 0
            ? []
            : (await _orm.Select<AllianceMemberEntity>()
                .Where(m => ids.Contains(m.AllianceId))
                .ToListAsync(cancellationToken))
            .GroupBy(m => m.AllianceId)
            .ToDictionary(g => g.Key, g => g.Count());
        var leaders = rows.Select(a => a.LeaderCharacterId).Distinct().ToArray();
        var leaderNames = leaders.Length == 0
            ? []
            : (await _orm.Select<CharacterEntity>()
                .Where(c => leaders.Contains(c.Id))
                .ToListAsync(cancellationToken))
            .ToDictionary(c => c.Id, c => c.Name);

        return new PagedResult<AllianceSummaryDto>
        {
            Items = rows.Select(a => new AllianceSummaryDto(
                a.Id,
                a.Name,
                counts.GetValueOrDefault(a.Id),
                leaderNames.GetValueOrDefault(a.LeaderCharacterId, ""))).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            Total = total
        };
    }

    public async Task<AllianceDetailDto?> GetMineAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        var member = await FindMemberAsync(character.Id, cancellationToken);
        return member is null
            ? null
            : await GetDetailAsync(member.AllianceId, character.Id, cancellationToken);
    }

    public async Task<AllianceDetailDto> GetAsync(long accountId, long allianceId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        return await GetDetailAsync(allianceId, character.Id, cancellationToken);
    }

    public async Task<AlliancePendingDto> GetPendingAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        var invites = await _orm.Select<AllianceInviteEntity>()
            .Where(i => i.TargetCharacterId == character.Id && i.Status == AllianceRequestStatus.Pending)
            .ToListAsync(cancellationToken);
        var inviteAllianceIds = invites.Select(i => i.AllianceId).Distinct().ToArray();
        var inviteAlliances = inviteAllianceIds.Length == 0
            ? []
            : (await _orm.Select<AllianceEntity>().Where(a => inviteAllianceIds.Contains(a.Id)).ToListAsync(cancellationToken))
                .ToDictionary(a => a.Id);
        var inviterIds = invites.Select(i => i.InviterCharacterId).Distinct().ToArray();
        var inviters = inviterIds.Length == 0
            ? []
            : (await _orm.Select<CharacterEntity>().Where(c => inviterIds.Contains(c.Id)).ToListAsync(cancellationToken))
                .ToDictionary(c => c.Id, c => c.Name);

        var applications = new List<AllianceApplicationDto>();
        var member = await FindMemberAsync(character.Id, cancellationToken);
        if (member is { Role: AllianceRole.Leader or AllianceRole.Officer })
        {
            var apps = await _orm.Select<AllianceApplicationEntity>()
                .Where(a => a.AllianceId == member.AllianceId && a.Status == AllianceRequestStatus.Pending)
                .ToListAsync(cancellationToken);
            var applicantIds = apps.Select(a => a.CharacterId).Distinct().ToArray();
            var names = applicantIds.Length == 0
                ? []
                : (await _orm.Select<CharacterEntity>().Where(c => applicantIds.Contains(c.Id)).ToListAsync(cancellationToken))
                    .ToDictionary(c => c.Id, c => c.Name);
            applications = apps.Select(a => new AllianceApplicationDto(
                a.Id,
                a.AllianceId,
                a.CharacterId,
                names.GetValueOrDefault(a.CharacterId, ""),
                a.CreatedAt)).ToList();
        }

        return new AlliancePendingDto(
            invites.Select(i => new AllianceInviteDto(
                i.Id,
                i.AllianceId,
                inviteAlliances.GetValueOrDefault(i.AllianceId)?.Name ?? "",
                inviters.GetValueOrDefault(i.InviterCharacterId, ""),
                i.CreatedAt)).ToList(),
            applications);
    }

    public async Task ApplyAsync(long accountId, long allianceId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterWithCityAsync(accountId, cancellationToken);
        if (await FindMemberAsync(character.Id, cancellationToken) is not null)
        {
            throw new BizException(ErrorCodes.AlreadyInAlliance, "已加入联盟");
        }

        var alliance = await RequireAllianceAsync(allianceId, cancellationToken);
        await EnsureCapacityAsync(alliance.Id, cancellationToken);
        if (await _orm.Select<AllianceApplicationEntity>().AnyAsync(
                a => a.AllianceId == alliance.Id
                    && a.CharacterId == character.Id
                    && a.Status == AllianceRequestStatus.Pending,
                cancellationToken))
        {
            throw new BizException(ErrorCodes.Conflict, "已对该联盟发出申请");
        }

        await _orm.Insert(new AllianceApplicationEntity
        {
            AllianceId = alliance.Id,
            CharacterId = character.Id,
            Status = AllianceRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        }).ExecuteAffrowsAsync(cancellationToken);

        await _mail.SendAsync(
            alliance.LeaderCharacterId,
            MailType.Alliance,
            $"入盟申请：{character.Name}",
            $"{character.Name} 申请加入 {alliance.Name}",
            "application",
            alliance.Id,
            cancellationToken);
    }

    public async Task InviteAsync(long accountId, InviteAllianceRequest request, CancellationToken cancellationToken)
    {
        var (alliance, actor) = await RequireOfficerAsync(accountId, cancellationToken);
        var inviter = await RequireCharacterAsync(accountId, cancellationToken);
        await EnsureCapacityAsync(alliance.Id, cancellationToken);
        var name = request.CharacterName.Trim();
        var target = await _orm.Select<CharacterEntity>().Where(c => c.Name == name).FirstAsync(cancellationToken)
            ?? throw new BizException(ErrorCodes.NotFound, "角色不存在");
        if (target.Id == actor.CharacterId)
        {
            throw new BizException(ErrorCodes.ValidationFailed, "不能邀请自己");
        }

        if (await FindMemberAsync(target.Id, cancellationToken) is not null)
        {
            throw new BizException(ErrorCodes.AlreadyInAlliance, "对方已加入联盟");
        }

        if (await _orm.Select<AllianceInviteEntity>().AnyAsync(
                i => i.AllianceId == alliance.Id
                    && i.TargetCharacterId == target.Id
                    && i.Status == AllianceRequestStatus.Pending,
                cancellationToken))
        {
            throw new BizException(ErrorCodes.Conflict, "已向该角色发出邀请");
        }

        var invite = new AllianceInviteEntity
        {
            AllianceId = alliance.Id,
            InviterCharacterId = actor.CharacterId,
            TargetCharacterId = target.Id,
            Status = AllianceRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        invite.Id = await _orm.Insert(invite).ExecuteIdentityAsync(cancellationToken);
        await _mail.SendAsync(
            target.Id,
            MailType.Alliance,
            $"联盟邀请：{alliance.Name}",
            $"{inviter.Name} 邀请你加入 {alliance.Name}",
            "invite",
            invite.Id,
            cancellationToken);
    }

    public async Task AcceptInviteAsync(long accountId, long inviteId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterWithCityAsync(accountId, cancellationToken);
        var invite = await _orm.Select<AllianceInviteEntity>().Where(i => i.Id == inviteId).FirstAsync(cancellationToken)
            ?? throw new BizException(ErrorCodes.NotFound, "邀请不存在");
        if (invite.TargetCharacterId != character.Id || invite.Status != AllianceRequestStatus.Pending)
        {
            throw new BizException(ErrorCodes.AllianceInviteInvalid, "邀请已失效");
        }

        await JoinAsync(character, invite.AllianceId, cancellationToken);
        invite.Status = AllianceRequestStatus.Accepted;
        await _orm.Update<AllianceInviteEntity>()
            .SetSource(invite)
            .UpdateColumns(i => i.Status)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    public async Task DeclineInviteAsync(long accountId, long inviteId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        var invite = await _orm.Select<AllianceInviteEntity>().Where(i => i.Id == inviteId).FirstAsync(cancellationToken)
            ?? throw new BizException(ErrorCodes.NotFound, "邀请不存在");
        if (invite.TargetCharacterId != character.Id || invite.Status != AllianceRequestStatus.Pending)
        {
            throw new BizException(ErrorCodes.AllianceInviteInvalid, "邀请已失效");
        }

        invite.Status = AllianceRequestStatus.Declined;
        await _orm.Update<AllianceInviteEntity>()
            .SetSource(invite)
            .UpdateColumns(i => i.Status)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    public async Task AcceptApplicationAsync(long accountId, long applicationId, CancellationToken cancellationToken)
    {
        var (alliance, _) = await RequireOfficerAsync(accountId, cancellationToken);
        var application = await _orm.Select<AllianceApplicationEntity>()
            .Where(a => a.Id == applicationId)
            .FirstAsync(cancellationToken)
            ?? throw new BizException(ErrorCodes.NotFound, "申请不存在");
        if (application.AllianceId != alliance.Id || application.Status != AllianceRequestStatus.Pending)
        {
            throw new BizException(ErrorCodes.AllianceInviteInvalid, "申请已失效");
        }

        var applicant = await _orm.Select<CharacterEntity>()
            .Where(c => c.Id == application.CharacterId)
            .FirstAsync(cancellationToken)
            ?? throw new BizException(ErrorCodes.NotFound, "角色不存在");
        await JoinAsync(applicant, alliance.Id, cancellationToken);
        application.Status = AllianceRequestStatus.Accepted;
        await _orm.Update<AllianceApplicationEntity>()
            .SetSource(application)
            .UpdateColumns(a => a.Status)
            .ExecuteAffrowsAsync(cancellationToken);
        await _mail.SendAsync(
            applicant.Id,
            MailType.Alliance,
            $"已加入 {alliance.Name}",
            $"你的入盟申请已通过",
            "alliance",
            alliance.Id,
            cancellationToken);
    }

    public async Task RejectApplicationAsync(long accountId, long applicationId, CancellationToken cancellationToken)
    {
        var (alliance, _) = await RequireOfficerAsync(accountId, cancellationToken);
        var application = await _orm.Select<AllianceApplicationEntity>()
            .Where(a => a.Id == applicationId)
            .FirstAsync(cancellationToken)
            ?? throw new BizException(ErrorCodes.NotFound, "申请不存在");
        if (application.AllianceId != alliance.Id || application.Status != AllianceRequestStatus.Pending)
        {
            throw new BizException(ErrorCodes.AllianceInviteInvalid, "申请已失效");
        }

        application.Status = AllianceRequestStatus.Declined;
        await _orm.Update<AllianceApplicationEntity>()
            .SetSource(application)
            .UpdateColumns(a => a.Status)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    public async Task LeaveAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        var member = await FindMemberAsync(character.Id, cancellationToken)
            ?? throw new BizException(ErrorCodes.NotInAlliance, "未加入联盟");
        var alliance = await RequireAllianceAsync(member.AllianceId, cancellationToken);
        var members = await _orm.Select<AllianceMemberEntity>()
            .Where(m => m.AllianceId == alliance.Id)
            .ToListAsync(cancellationToken);
        var dissolveByLeave = member.Role == AllianceRole.Leader && members.Count == 1;

        await InTransactionAsync(async transaction =>
        {
            if (dissolveByLeave)
            {
                await DissolveInternalAsync(alliance, cancellationToken, transaction);
                return 0;
            }

            if (member.Role == AllianceRole.Leader)
            {
                var successor = members
                    .Where(m => m.CharacterId != member.CharacterId)
                    .OrderBy(m => m.Role == AllianceRole.Officer ? 0 : 1)
                    .ThenBy(m => m.JoinedAt)
                    .ThenBy(m => m.Id)
                    .First();
                successor.Role = AllianceRole.Leader;
                alliance.LeaderCharacterId = successor.CharacterId;
                await _orm.Update<AllianceEntity>()
                    .WithTransaction(transaction)
                    .SetSource(alliance)
                    .UpdateColumns(a => a.LeaderCharacterId)
                    .ExecuteAffrowsAsync(cancellationToken);
                await _orm.Update<AllianceMemberEntity>()
                    .WithTransaction(transaction)
                    .SetSource(successor)
                    .UpdateColumns(m => m.Role)
                    .ExecuteAffrowsAsync(cancellationToken);
            }

            await _orm.Delete<AllianceMemberEntity>()
                .WithTransaction(transaction)
                .Where(m => m.Id == member.Id)
                .ExecuteAffrowsAsync(cancellationToken);
            return 0;
        }, cancellationToken);

        if (dissolveByLeave)
        {
            await _mail.SendManyAsync(
                members.Select(m => m.CharacterId).ToList(),
                MailType.Alliance,
                $"{alliance.Name} 已解散",
                "联盟已解散",
                "alliance",
                alliance.Id,
                cancellationToken);
        }
    }

    public async Task KickAsync(long accountId, KickAllianceRequest request, CancellationToken cancellationToken)
    {
        var (alliance, actor) = await RequireOfficerAsync(accountId, cancellationToken);
        if (request.CharacterId == actor.CharacterId)
        {
            throw new BizException(ErrorCodes.ValidationFailed, "不能踢出自己，请使用退出");
        }

        var target = await _orm.Select<AllianceMemberEntity>()
            .Where(m => m.AllianceId == alliance.Id && m.CharacterId == request.CharacterId)
            .FirstAsync(cancellationToken)
            ?? throw new BizException(ErrorCodes.NotFound, "该成员不在联盟中");
        if (!CanManage(actor.Role, target.Role))
        {
            throw new BizException(ErrorCodes.AlliancePermission, "无权踢出该成员");
        }

        await _orm.Delete<AllianceMemberEntity>().Where(m => m.Id == target.Id).ExecuteAffrowsAsync(cancellationToken);
        await _mail.SendAsync(
            target.CharacterId,
            MailType.Alliance,
            $"已离开 {alliance.Name}",
            "你已被移出联盟",
            "alliance",
            alliance.Id,
            cancellationToken);
    }

    public async Task UpdateNoticeAsync(long accountId, UpdateAllianceNoticeRequest request, CancellationToken cancellationToken)
    {
        var (alliance, _) = await RequireOfficerAsync(accountId, cancellationToken);
        alliance.Notice = (request.Notice ?? "").Trim();
        if (alliance.Notice.Length > AllianceRules.NoticeMaxLength)
        {
            throw new BizException(ErrorCodes.ValidationFailed, "公告过长");
        }

        await _orm.Update<AllianceEntity>()
            .SetSource(alliance)
            .UpdateColumns(a => a.Notice)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    public async Task DissolveAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        var member = await FindMemberAsync(character.Id, cancellationToken)
            ?? throw new BizException(ErrorCodes.NotInAlliance, "未加入联盟");
        if (member.Role != AllianceRole.Leader)
        {
            throw new BizException(ErrorCodes.AlliancePermission, "只有盟主可以解散联盟");
        }

        var alliance = await RequireAllianceAsync(member.AllianceId, cancellationToken);
        var memberIds = (await _orm.Select<AllianceMemberEntity>()
            .Where(m => m.AllianceId == alliance.Id)
            .ToListAsync(cancellationToken))
            .Select(m => m.CharacterId)
            .ToList();
        await InTransactionAsync(async transaction =>
        {
            await DissolveInternalAsync(alliance, cancellationToken, transaction);
            return 0;
        }, cancellationToken);
        await _mail.SendManyAsync(
            memberIds,
            MailType.Alliance,
            $"{alliance.Name} 已解散",
            "联盟已解散",
            "alliance",
            alliance.Id,
            cancellationToken);
    }

    private async Task JoinAsync(CharacterEntity character, long allianceId, CancellationToken cancellationToken)
    {
        if (await FindMemberAsync(character.Id, cancellationToken) is not null)
        {
            throw new BizException(ErrorCodes.AlreadyInAlliance, "已加入联盟");
        }

        try
        {
            await InTransactionAsync(async transaction =>
            {
                await EnsureCapacityAsync(allianceId, cancellationToken, transaction);
                var already = await _orm.Select<AllianceMemberEntity>()
                    .WithTransaction(transaction)
                    .Where(m => m.CharacterId == character.Id)
                    .AnyAsync(cancellationToken);
                if (already)
                {
                    throw new BizException(ErrorCodes.AlreadyInAlliance, "已加入联盟");
                }

                await _orm.Insert(new AllianceMemberEntity
                {
                    AllianceId = allianceId,
                    CharacterId = character.Id,
                    Role = AllianceRole.Member,
                    JoinedAt = DateTime.UtcNow
                }).WithTransaction(transaction).ExecuteAffrowsAsync(cancellationToken);
                await CancelPendingForCharacterAsync(character.Id, cancellationToken, transaction);
                return 0;
            }, cancellationToken);
        }
        catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new BizException(ErrorCodes.AlreadyInAlliance, "已加入联盟");
        }
    }

    private async Task DissolveInternalAsync(
        AllianceEntity alliance,
        CancellationToken cancellationToken,
        DbTransaction transaction)
    {
        await _orm.Delete<AllianceMemberEntity>()
            .WithTransaction(transaction)
            .Where(m => m.AllianceId == alliance.Id)
            .ExecuteAffrowsAsync(cancellationToken);
        await _orm.Delete<AllianceInviteEntity>()
            .WithTransaction(transaction)
            .Where(i => i.AllianceId == alliance.Id)
            .ExecuteAffrowsAsync(cancellationToken);
        await _orm.Delete<AllianceApplicationEntity>()
            .WithTransaction(transaction)
            .Where(a => a.AllianceId == alliance.Id)
            .ExecuteAffrowsAsync(cancellationToken);
        await _orm.Delete<AllianceEntity>()
            .WithTransaction(transaction)
            .Where(a => a.Id == alliance.Id)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    private async Task CancelPendingForCharacterAsync(
        long characterId,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null)
    {
        var invites = _orm.Update<AllianceInviteEntity>()
            .Where(i => i.TargetCharacterId == characterId && i.Status == AllianceRequestStatus.Pending)
            .Set(i => i.Status, AllianceRequestStatus.Declined);
        var applications = _orm.Update<AllianceApplicationEntity>()
            .Where(a => a.CharacterId == characterId && a.Status == AllianceRequestStatus.Pending)
            .Set(a => a.Status, AllianceRequestStatus.Declined);
        if (transaction is not null)
        {
            invites = invites.WithTransaction(transaction);
            applications = applications.WithTransaction(transaction);
        }

        await invites.ExecuteAffrowsAsync(cancellationToken);
        await applications.ExecuteAffrowsAsync(cancellationToken);
    }

    private async Task<T> InTransactionAsync<T>(
        Func<DbTransaction, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var conn = await _orm.Ado.MasterPool.GetAsync();
        await using var transaction = await conn.Value.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await action(transaction);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task EnsureCapacityAsync(
        long allianceId,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null)
    {
        var alliance = _orm.Select<AllianceEntity>().Where(a => a.Id == allianceId);
        var members = _orm.Select<AllianceMemberEntity>().Where(m => m.AllianceId == allianceId);
        if (transaction is not null)
        {
            alliance = alliance.WithTransaction(transaction).ForUpdate();
            members = members.WithTransaction(transaction);
        }

        if (await alliance.FirstAsync(cancellationToken) is null)
        {
            throw new BizException(ErrorCodes.NotFound, "联盟不存在");
        }

        var count = (int)await members.CountAsync(cancellationToken);
        if (count >= AllianceRules.MaxMembers)
        {
            throw new BizException(ErrorCodes.AllianceFull, "联盟人数已满");
        }
    }

    private async Task<(AllianceEntity Alliance, AllianceMemberEntity Member)> RequireOfficerAsync(
        long accountId,
        CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        var member = await FindMemberAsync(character.Id, cancellationToken)
            ?? throw new BizException(ErrorCodes.NotInAlliance, "未加入联盟");
        if (member.Role is not AllianceRole.Leader and not AllianceRole.Officer)
        {
            throw new BizException(ErrorCodes.AlliancePermission, "需要官员或盟主权限");
        }

        var alliance = await RequireAllianceAsync(member.AllianceId, cancellationToken);
        return (alliance, member);
    }

    private async Task<AllianceDetailDto> GetDetailAsync(long allianceId, long viewerCharacterId, CancellationToken cancellationToken)
    {
        var alliance = await RequireAllianceAsync(allianceId, cancellationToken);
        var members = await _orm.Select<AllianceMemberEntity>()
            .Where(m => m.AllianceId == allianceId)
            .ToListAsync(cancellationToken);
        var characterIds = members.Select(m => m.CharacterId).ToArray();
        var names = characterIds.Length == 0
            ? []
            : (await _orm.Select<CharacterEntity>().Where(c => characterIds.Contains(c.Id)).ToListAsync(cancellationToken))
                .ToDictionary(c => c.Id, c => c.Name);
        var cities = characterIds.Length == 0
            ? []
            : (await _orm.Select<CityEntity>().Where(c => characterIds.Contains(c.CharacterId)).ToListAsync(cancellationToken))
                .ToDictionary(c => c.CharacterId, c => c.Id);
        var myRole = members.FirstOrDefault(m => m.CharacterId == viewerCharacterId)?.Role;
        return new AllianceDetailDto(
            alliance.Id,
            alliance.Name,
            alliance.Notice,
            alliance.LeaderCharacterId,
            members.Count,
            myRole,
            members
                .OrderBy(m => m.Role)
                .ThenBy(m => m.JoinedAt)
                .Select(m => new AllianceMemberDto(
                    m.CharacterId,
                    names.GetValueOrDefault(m.CharacterId, ""),
                    m.Role,
                    m.JoinedAt,
                    cities.GetValueOrDefault(m.CharacterId)))
                .ToList());
    }

    private Task<AllianceMemberEntity?> FindMemberAsync(long characterId, CancellationToken cancellationToken) =>
        _orm.Select<AllianceMemberEntity>().Where(m => m.CharacterId == characterId).ToOneAsync(cancellationToken)!;

    private async Task<AllianceEntity> RequireAllianceAsync(long allianceId, CancellationToken cancellationToken)
    {
        var alliance = await _orm.Select<AllianceEntity>().Where(a => a.Id == allianceId).FirstAsync(cancellationToken);
        return alliance ?? throw new BizException(ErrorCodes.NotFound, "联盟不存在");
    }

    private async Task<CharacterEntity> RequireCharacterAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await _orm.Select<CharacterEntity>()
            .Where(c => c.AccountId == accountId)
            .FirstAsync(cancellationToken);
        return character ?? throw new BizException(ErrorCodes.NotFound, "尚未创建角色");
    }

    private async Task<CharacterEntity> RequireCharacterWithCityAsync(long accountId, CancellationToken cancellationToken)
    {
        var character = await RequireCharacterAsync(accountId, cancellationToken);
        if (!await _orm.Select<CityEntity>().AnyAsync(c => c.CharacterId == character.Id, cancellationToken))
        {
            throw new BizException(ErrorCodes.NotFound, "尚未建立主城");
        }

        return character;
    }

    private static void ValidateName(string name)
    {
        if (name.Length is < AllianceRules.NameMinLength or > AllianceRules.NameMaxLength)
        {
            throw new BizException(ErrorCodes.ValidationFailed, "联盟名长度为 2～12 位");
        }
    }

    private static string NormalizeName(string name) => name.Trim().ToLowerInvariant();

    private static bool CanManage(AllianceRole actor, AllianceRole target) =>
        actor == AllianceRole.Leader && target != AllianceRole.Leader
        || actor == AllianceRole.Officer && target == AllianceRole.Member;
}
