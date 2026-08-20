using System.Net;
using SanguoGame.Core;
using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.Social;
using SanguoGame.Server.Contracts;
using Xunit;

namespace SanguoGame.Tests.Api;

[Collection("api")]
public sealed class AllApiDataTests
{
    private readonly GameApiFactory _factory;

    public AllApiDataTests(GameApiFactory factory)
    {
        _factory = factory;
    }

    [SkippableFact]
    public async Task Auth_Logout_LoginFailure_And_SessionAfterCity()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var username = "u" + Guid.NewGuid().ToString("N")[..10];
        var tokens = await api.RegisterAsync(username);

        var wrong = new ApiClient(_factory.CreateJsonClient());
        var (wrongStatus, wrongBody) = await wrong.Post<TokenResponse>(
            "/api/auth/login",
            new { username, password = "wrong-pass" });
        Assert.Equal(HttpStatusCode.OK, wrongStatus);
        Assert.Equal(ErrorCodes.Unauthorized, wrongBody.Code);

        var reserved = new ApiClient(_factory.CreateJsonClient());
        var (_, reservedBody) = await reserved.Post<TokenResponse>(
            "/api/auth/register",
            new { username = "ai_player1", password = "Passw0rd!" });
        Assert.Equal(ErrorCodes.ValidationFailed, reservedBody.Code);

        var tag = Guid.NewGuid().ToString("N")[..8];
        var (_, character) = await api.Post<CharacterResponse>("/api/characters", new { name = "角" + tag });
        Assert.Equal(0, character.Code);
        var (_, city) = await api.Post<CityResponse>("/api/city/found");
        Assert.Equal(0, city.Code);

        var (_, session) = await api.Get<SessionResponse>("/api/auth/me");
        Assert.Equal(0, session.Code);
        Assert.Equal(username, session.Data?.Username);
        Assert.Equal(character.Data?.Id, session.Data?.Character?.Id);
        Assert.Equal(city.Data?.Id, session.Data?.City?.Id);
        Assert.Equal(city.Data?.X, session.Data?.City?.X);
        Assert.Equal(city.Data?.Y, session.Data?.City?.Y);

        var (_, me) = await api.Get<CharacterResponse>("/api/characters/me");
        Assert.Equal(0, me.Code);
        Assert.Equal(character.Data?.Name, me.Data?.Name);

        var (_, logout) = await api.Post<object?>("/api/auth/logout", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(0, logout.Code);
        var (_, reuse) = await api.Post<TokenResponse>("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(ErrorCodes.Unauthorized, reuse.Code);
    }

    [SkippableFact]
    public async Task CharacterName_Taken_And_CityFoundData()
    {
        SkipIfUnavailable();
        var first = new ApiClient(_factory.CreateJsonClient());
        var player = await first.RegisterPlayerAsync();

        var second = new ApiClient(_factory.CreateJsonClient());
        await second.RegisterAsync("u" + Guid.NewGuid().ToString("N")[..10]);
        var (_, taken) = await second.Post<CharacterResponse>("/api/characters", new { name = player.CharacterName });
        Assert.Equal(ErrorCodes.CharacterNameTaken, taken.Code);

        var (_, city) = await first.Get<CityResponse>("/api/city/me");
        Assert.Equal(0, city.Code);
        Assert.Equal($"{player.CharacterName}的城", city.Data?.Name);
        Assert.Equal(player.CharacterId, city.Data?.CharacterId);
        Assert.Empty(city.Data!.Zones.Inner);
        Assert.Empty(city.Data.Zones.Wall);
        Assert.Empty(city.Data.Zones.Outer);
        Assert.InRange(city.Data.X, 0, 39);
        Assert.InRange(city.Data.Y, 0, 39);
    }

    [SkippableFact]
    public async Task Buildings_StartState_CostDeduction_And_Queue()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var player = await api.RegisterPlayerAsync();

        var (_, overview) = await api.Get<BuildingsOverviewDto>("/api/buildings");
        Assert.Equal(0, overview.Code);
        Assert.Equal(player.CityId, overview.Data?.CityId);
        Assert.Equal(InnerBuildingCatalog.StartingResource, overview.Data?.Resources.Grain);
        Assert.Equal(InnerBuildingCatalog.StartingResource, overview.Data?.Resources.Wood);
        Assert.Equal(InnerBuildingCatalog.StartingResource, overview.Data?.Resources.Iron);
        Assert.Equal(InnerBuildingCatalog.StartingResource, overview.Data?.Resources.Copper);
        Assert.Equal(InnerBuildingCatalog.DefaultResourceCap, overview.Data?.ResourceCap);
        Assert.Equal(InnerBuildingCatalog.PopulationCap(0), overview.Data?.PopulationCap);
        Assert.Null(overview.Data?.Queue);
        Assert.Equal(InnerBuildingCatalog.All.Count, overview.Data?.Buildings.Count);

        var palace = overview.Data!.Buildings.Single(b => b.Type == "palace");
        Assert.Equal(0, palace.Level);
        Assert.Equal("主殿", palace.Name);
        Assert.Equal(BuildingCategory.Civil, palace.Category);
        Assert.Equal(15, palace.Next?.DurationSeconds);
        Assert.Equal(new ResourceDto(200, 200, 80, 40), palace.Next?.Cost);
        Assert.Null(palace.BlockedReason);

        var house = overview.Data.Buildings.Single(b => b.Type == "house");
        Assert.Equal("prerequisite", house.BlockedReason);
        var barracks = overview.Data.Buildings.Single(b => b.Type == "barracks");
        Assert.Equal("prerequisite", barracks.BlockedReason);

        var (_, unknown) = await api.Post<BuildingsOverviewDto>("/api/buildings/upgrade", new { buildingType = "nope" });
        Assert.Equal(ErrorCodes.ValidationFailed, unknown.Code);
        var (_, blockedHouse) = await api.Post<BuildingsOverviewDto>("/api/buildings/upgrade", new { buildingType = "house" });
        Assert.Equal(ErrorCodes.BuildingPrerequisite, blockedHouse.Code);

        var (_, upgrading) = await api.Post<BuildingsOverviewDto>("/api/buildings/upgrade", new { buildingType = "palace" });
        Assert.Equal(0, upgrading.Code);
        Assert.Equal("palace", upgrading.Data?.Queue?.BuildingType);
        Assert.Equal(1, upgrading.Data?.Queue?.TargetLevel);
        Assert.Equal(1800, upgrading.Data?.Resources.Grain);
        Assert.Equal(1800, upgrading.Data?.Resources.Wood);
        Assert.Equal(1920, upgrading.Data?.Resources.Iron);
        Assert.Equal(1960, upgrading.Data?.Resources.Copper);
        Assert.Equal(0, upgrading.Data?.Buildings.Single(b => b.Type == "palace").Level);
        Assert.Equal(BuildingStatus.Upgrading, upgrading.Data?.Buildings.Single(b => b.Type == "palace").Status);
        Assert.Equal("queue", upgrading.Data?.Buildings.Single(b => b.Type == "house").BlockedReason);

        var (_, fieldBusy) = await api.Post<FieldsOverviewDto>("/api/fields/upgrade", new { fieldType = "farm" });
        Assert.Equal(ErrorCodes.BuildingQueueBusy, fieldBusy.Code);

        await _factory.ForceCompleteBuildingsAsync();
        var (_, done) = await api.Get<BuildingsOverviewDto>("/api/buildings");
        Assert.Equal(1, done.Data?.Buildings.Single(b => b.Type == "palace").Level);
        Assert.Null(done.Data?.Queue);
        Assert.Null(done.Data?.Buildings.Single(b => b.Type == "house").BlockedReason);
        Assert.Equal("prerequisite", done.Data?.Buildings.Single(b => b.Type == "barracks").BlockedReason);
        Assert.Equal(InnerBuildingCatalog.PopulationCap(0), done.Data?.PopulationCap);
    }

    [SkippableFact]
    public async Task Fields_Catalog_Upgrade_CollectFormula_And_WarehouseFull()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        var player = await api.RegisterPlayerAsync();

        var (_, empty) = await api.Get<FieldsOverviewDto>("/api/fields");
        Assert.Equal(0, empty.Code);
        Assert.Equal(4, empty.Data?.Fields.Count);
        Assert.All(empty.Data!.Fields, field =>
        {
            Assert.Equal(0, field.Level);
            Assert.Equal(0, field.Pending);
            Assert.Equal("prerequisite", field.BlockedReason);
        });
        Assert.Equal("grain", empty.Data.Fields.Single(f => f.Type == "farm").Resource);
        Assert.Equal("wood", empty.Data.Fields.Single(f => f.Type == "lumber").Resource);

        var (_, tooSoon) = await api.Post<FieldsOverviewDto>("/api/fields/upgrade", new { fieldType = "farm" });
        Assert.Equal(ErrorCodes.BuildingPrerequisite, tooSoon.Code);

        await UpgradeAndFinish(api, "palace");
        var (_, farmUp) = await api.Post<FieldsOverviewDto>("/api/fields/upgrade", new { fieldType = "farm" });
        Assert.Equal(0, farmUp.Code);
        Assert.Equal("farm", farmUp.Data?.Queue?.BuildingType);
        Assert.Equal(1650, farmUp.Data?.Resources.Grain);
        Assert.Equal(1720, farmUp.Data?.Resources.Wood);
        Assert.Equal(1900, farmUp.Data?.Resources.Iron);
        Assert.Equal(1950, farmUp.Data?.Resources.Copper);
        await _factory.ForceCompleteBuildingsAsync();

        var (_, built) = await api.Get<FieldsOverviewDto>("/api/fields");
        var farm = built.Data!.Fields.Single(f => f.Type == "farm");
        Assert.Equal(1, farm.Level);
        Assert.Equal(600, farm.RatePerHour);
        Assert.Equal(1500, farm.FieldCap);
        Assert.NotNull(farm.LastCollectedAt);
        Assert.Equal(0, farm.Pending);

        await _factory.BackdateFieldAsync(player.CityId, "farm", TimeSpan.FromHours(1));
        var (_, pending) = await api.Get<FieldsOverviewDto>("/api/fields");
        Assert.Equal(600, pending.Data!.Fields.Single(f => f.Type == "farm").Pending);

        var (_, collected) = await api.Post<FieldsCollectDto>("/api/fields/collect", new { fieldType = "farm" });
        Assert.Equal(0, collected.Code);
        Assert.Equal("ok", collected.Message);
        Assert.Equal(600, collected.Data?.Collected.Grain);
        Assert.Equal(0, collected.Data?.Collected.Wood);
        Assert.Equal(2250, collected.Data?.Resources.Grain);
        Assert.Equal(0, collected.Data?.Fields.Single(f => f.Type == "farm").Pending);

        var (_, none) = await api.Post<FieldsCollectDto>("/api/fields/collect", new { });
        Assert.Equal(0, none.Code);
        Assert.Equal(0, none.Data?.Collected.Grain);

        await _factory.SetCityResourcesAsync(player.CityId, 8000, 1720, 1900, 1950);
        await _factory.BackdateFieldAsync(player.CityId, "farm", TimeSpan.FromHours(1));
        var (_, full) = await api.Post<FieldsCollectDto>("/api/fields/collect", new { fieldType = "farm" });
        Assert.Equal(0, full.Code);
        Assert.Equal("仓库已满", full.Message);
        Assert.Equal(0, full.Data?.Collected.Grain);
        Assert.Equal(8000, full.Data?.Resources.Grain);
        Assert.Equal(600, full.Data?.Fields.Single(f => f.Type == "farm").Pending);
    }

    [SkippableFact]
    public async Task Walls_StartState_Upgrade_And_DefenseNumbers()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        await api.RegisterPlayerAsync();

        var (_, start) = await api.Get<WallsOverviewDto>("/api/walls");
        Assert.Equal(0, start.Code);
        Assert.Equal(3, start.Data?.Walls.Count);
        Assert.Equal(0, start.Data?.WallDefense);
        Assert.Equal(0, start.Data?.TrapBonus);
        Assert.All(start.Data!.Walls, wall =>
        {
            Assert.Equal(0, wall.Level);
            Assert.Equal(BuildingCategory.Wall, wall.Category);
            Assert.Equal("prerequisite", wall.BlockedReason);
        });

        var (_, blocked) = await api.Post<WallsOverviewDto>("/api/walls/upgrade", new { wallType = "arrowTower" });
        Assert.Equal(ErrorCodes.BuildingPrerequisite, blocked.Code);

        await UpgradeAndFinish(api, "palace");
        await UpgradeAndFinish(api, "palace");
        var (_, tower) = await api.Post<WallsOverviewDto>("/api/walls/upgrade", new { wallType = "arrowTower" });
        Assert.Equal(0, tower.Code);
        Assert.Equal("arrowTower", tower.Data?.Queue?.BuildingType);
        await _factory.ForceCompleteBuildingsAsync();

        var (_, done) = await api.Get<WallsOverviewDto>("/api/walls");
        Assert.Equal(8, done.Data?.WallDefense);
        Assert.Equal(0, done.Data?.TrapBonus);
        var built = done.Data!.Walls.Single(w => w.Type == "arrowTower");
        Assert.Equal(1, built.Level);
        Assert.Equal(8, built.Effects["wallDefense"]);
        Assert.Equal("prerequisite", done.Data.Walls.Single(w => w.Type == "trap").BlockedReason);
    }

    [SkippableFact]
    public async Task Army_World_RecruitCaps_SelfAttack_And_PvpLoot()
    {
        SkipIfUnavailable();
        var attacker = new ApiClient(_factory.CreateJsonClient());
        var atk = await attacker.RegisterPlayerAsync("atk");
        var defender = new ApiClient(_factory.CreateJsonClient());
        var def = await defender.RegisterPlayerAsync("def");

        var (_, army0) = await attacker.Get<ArmyOverviewDto>("/api/army");
        Assert.Equal(0, army0.Code);
        Assert.Equal(0, army0.Data?.Troops.Infantry);
        Assert.Equal(InnerBuildingCatalog.TroopCap(0), army0.Data?.TroopCap);
        Assert.Equal(3, army0.Data?.TroopTypes.Count);
        Assert.Contains(army0.Data!.TroopTypes, t => t.Type == "archer" && t.RequireBarracksLevel == 2);

        var (_, world0) = await attacker.Get<WorldDto>("/api/world");
        Assert.Equal(0, world0.Code);
        Assert.Equal(atk.X, world0.Data?.Origin.X);
        Assert.Equal(atk.Y, world0.Data?.Origin.Y);
        Assert.Contains(world0.Data!.Cities, c => c.Id == atk.CityId && c.Owner == "self" && !c.Protected);
        Assert.Contains(world0.Data.Cities, c => c.Id == def.CityId && c.Owner == "player");

        var (_, self) = await attacker.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "city",
            targetId = atk.CityId,
            infantry = 1,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(ErrorCodes.CannotAttackSelf, self.Code);

        await UpgradeAndFinish(attacker, "palace");
        await UpgradeAndFinish(attacker, "palace");
        await UpgradeAndFinish(attacker, "barracks");
        var (_, recruited) = await attacker.Post<ArmyOverviewDto>(
            "/api/army/recruit",
            new { troopType = "infantry", count = 40 });
        Assert.Equal(0, recruited.Code);
        Assert.Equal(40, recruited.Data?.Troops.Infantry);
        Assert.Equal(InnerBuildingCatalog.TroopCap(1), recruited.Data?.TroopCap);
        Assert.Equal(520, recruited.Data?.Resources.Grain);

        var (_, archerBlocked) = await attacker.Post<ArmyOverviewDto>(
            "/api/army/recruit",
            new { troopType = "archer", count = 1 });
        Assert.Equal(ErrorCodes.BarracksRequired, archerBlocked.Code);

        var (_, cap) = await attacker.Post<ArmyOverviewDto>(
            "/api/army/recruit",
            new { troopType = "infantry", count = 40 });
        Assert.Equal(ErrorCodes.TroopCapExceeded, cap.Code);

        var (_, marched) = await attacker.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "city",
            targetId = def.CityId,
            infantry = 40,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(0, marched.Code);
        Assert.Equal(0, marched.Data?.Troops.Infantry);
        var march = Assert.Single(marched.Data!.Marches);
        Assert.Equal(MarchTargetType.City, march.TargetType);
        Assert.Equal(def.CityId, march.TargetId);
        Assert.Equal(atk.X, march.FromX);
        Assert.Equal(def.X, march.ToX);
        Assert.True(march.Mine);
        Assert.Equal(40, march.Troops?.Infantry);

        var (_, worldMarch) = await defender.Get<WorldDto>("/api/world");
        var seen = worldMarch.Data!.Marches.Single(m => m.Id == march.Id);
        Assert.False(seen.Mine);
        Assert.Null(seen.Troops);

        await _factory.ForceCompleteMarchesAsync();

        var (_, atkReports) = await attacker.Get<PagedResult<BattleReportDto>>("/api/reports?page=1&pageSize=20");
        Assert.Equal(0, atkReports.Code);
        Assert.Equal(1, atkReports.Data?.Page);
        Assert.Equal(20, atkReports.Data?.PageSize);
        Assert.True(atkReports.Data?.Total >= 1);
        var report = atkReports.Data!.Items[0];
        Assert.True(report.AttackerWon);
        Assert.Equal(atk.CityId, report.AttackerCityId);
        Assert.Equal(MarchTargetType.City, report.DefenderType);
        Assert.Equal(def.CityId, report.DefenderId);
        Assert.Equal(40, report.AttackerBefore.Infantry);
        Assert.Equal(0, report.DefenderBefore.Infantry);
        Assert.Equal(600, report.Loot.Grain);
        Assert.Equal(600, report.Loot.Wood);
        Assert.Equal(600, report.Loot.Iron);
        Assert.Equal(600, report.Loot.Copper);

        var (_, defReports) = await defender.Get<PagedResult<BattleReportDto>>("/api/reports?page=1&pageSize=20");
        Assert.Contains(defReports.Data!.Items, r => r.Id == report.Id);

        var (_, defArmy) = await defender.Get<ArmyOverviewDto>("/api/army");
        Assert.Equal(1400, defArmy.Data?.Resources.Grain);
        Assert.NotNull(defArmy.Data?.ProtectionUntil);

        var (_, atkArmy) = await attacker.Get<ArmyOverviewDto>("/api/army");
        Assert.Equal(1120, atkArmy.Data?.Resources.Grain);
        Assert.True(atkArmy.Data!.Troops.Infantry < 40);
        Assert.Empty(atkArmy.Data.Marches);

        var (_, worldAfter) = await attacker.Get<WorldDto>("/api/world");
        Assert.Contains(worldAfter.Data!.Cities, c => c.Id == def.CityId && c.Protected);

        var (_, protectedMarch) = await attacker.Post<ArmyOverviewDto>("/api/army/march", new
        {
            targetType = "city",
            targetId = def.CityId,
            infantry = 1,
            archer = 0,
            cavalry = 0
        });
        Assert.Equal(ErrorCodes.CityProtected, protectedMarch.Code);

        var (_, mail) = await defender.Get<MailListDto>("/api/mail?unreadOnly=true");
        Assert.True(mail.Data!.UnreadCount >= 1);
        Assert.Contains(mail.Data.Items, m => m.Type == MailType.Battle && m.RelatedType == "report" && m.RelatedId == report.Id);

        var (_, readAll) = await defender.Post<object?>("/api/mail/read-all");
        Assert.Equal(0, readAll.Code);
        var (_, mail2) = await defender.Get<MailListDto>("/api/mail");
        Assert.Equal(0, mail2.Data!.UnreadCount);

        var (_, missing) = await defender.Post<object?>("/api/mail/999999/read");
        Assert.Equal(ErrorCodes.NotFound, missing.Code);

        var (_, troopsRank) = await attacker.Get<RankingDto>("/api/rankings?type=troops");
        Assert.Equal(RankingType.Troops, troopsRank.Data?.Type);
        Assert.Equal(atkArmy.Data.Troops.Infantry + atkArmy.Data.Troops.Archer + atkArmy.Data.Troops.Cavalry, troopsRank.Data?.MyScore);

        var (_, lootRank) = await attacker.Get<RankingDto>("/api/rankings?type=loot");
        Assert.Equal(RankingType.Loot, lootRank.Data?.Type);
        Assert.Equal(2400, lootRank.Data?.MyScore);
    }

    [SkippableFact]
    public async Task Alliance_List_Notice_Kick_Leave_Decline_Reject_Dissolve()
    {
        SkipIfUnavailable();
        var leader = new ApiClient(_factory.CreateJsonClient());
        var lead = await leader.RegisterPlayerAsync("ld");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var name = "测" + tag;
        var (_, created) = await leader.Post<AllianceDetailDto>("/api/alliances", new { name });
        Assert.Equal(0, created.Code);
        Assert.Equal(AllianceRole.Leader, created.Data?.MyRole);
        Assert.Equal(lead.CharacterId, created.Data?.LeaderCharacterId);
        Assert.Equal("", created.Data?.Notice);

        var (_, dup) = await leader.Post<AllianceDetailDto>("/api/alliances", new { name = "另" + tag });
        Assert.Equal(ErrorCodes.AlreadyInAlliance, dup.Code);

        var otherLeader = new ApiClient(_factory.CreateJsonClient());
        await otherLeader.RegisterPlayerAsync("ol");
        var (_, nameTaken) = await otherLeader.Post<AllianceDetailDto>("/api/alliances", new { name });
        Assert.Equal(ErrorCodes.AllianceNameTaken, nameTaken.Code);

        var (_, notice) = await leader.Post<object?>("/api/alliances/notice", new { notice = "今晚攻城" });
        Assert.Equal(0, notice.Code);
        var (_, mine) = await leader.Get<AllianceDetailDto>("/api/alliances/me");
        Assert.Equal("今晚攻城", mine.Data?.Notice);

        var (_, list) = await leader.Get<PagedResult<AllianceSummaryDto>>("/api/alliances?page=1&pageSize=100");
        Assert.Equal(0, list.Code);
        Assert.Equal(1, list.Data?.Page);
        Assert.Contains(list.Data!.Items, a => a.Id == created.Data!.Id && a.MemberCount == 1 && a.LeaderName == lead.CharacterName);

        var guest = new ApiClient(_factory.CreateJsonClient());
        var g = await guest.RegisterPlayerAsync("gs");
        var (_, detail) = await guest.Get<AllianceDetailDto>($"/api/alliances/{created.Data!.Id}");
        Assert.Equal(0, detail.Code);
        Assert.Null(detail.Data?.MyRole);
        Assert.Equal(1, detail.Data?.MemberCount);

        var (_, none) = await guest.Get<AllianceDetailDto>("/api/alliances/me");
        Assert.Equal(ErrorCodes.NotInAlliance, none.Code);

        var (_, invited) = await leader.Post<object?>("/api/alliances/invite", new { characterName = g.CharacterName });
        Assert.Equal(0, invited.Code);
        var (_, guestMail) = await guest.Get<MailListDto>("/api/mail");
        Assert.Contains(guestMail.Data!.Items, m => m.Type == MailType.Alliance && m.RelatedType == "invite");

        var (_, pendingInvite) = await guest.Get<AlliancePendingDto>("/api/alliances/pending");
        var invite = Assert.Single(pendingInvite.Data!.Invites);
        var (_, declined) = await guest.Post<object?>($"/api/alliances/invites/{invite.Id}/decline");
        Assert.Equal(0, declined.Code);
        var (_, pendingAfterDecline) = await guest.Get<AlliancePendingDto>("/api/alliances/pending");
        Assert.Empty(pendingAfterDecline.Data!.Invites);

        var (_, applied) = await guest.Post<object?>($"/api/alliances/{created.Data.Id}/apply");
        Assert.Equal(0, applied.Code);
        var (_, applyAgain) = await guest.Post<object?>($"/api/alliances/{created.Data.Id}/apply");
        Assert.Equal(ErrorCodes.Conflict, applyAgain.Code);

        var (_, pendingApps) = await leader.Get<AlliancePendingDto>("/api/alliances/pending");
        var application = Assert.Single(pendingApps.Data!.Applications);
        Assert.Equal(g.CharacterId, application.CharacterId);
        var (_, rejected) = await leader.Post<object?>($"/api/alliances/applications/{application.Id}/reject");
        Assert.Equal(0, rejected.Code);
        var (_, pendingRejected) = await leader.Get<AlliancePendingDto>("/api/alliances/pending");
        Assert.Empty(pendingRejected.Data!.Applications);

        var member = new ApiClient(_factory.CreateJsonClient());
        var m = await member.RegisterPlayerAsync("mb");
        var (_, applied2) = await member.Post<object?>($"/api/alliances/{created.Data.Id}/apply");
        Assert.Equal(0, applied2.Code);
        var (_, pending2) = await leader.Get<AlliancePendingDto>("/api/alliances/pending");
        var app2 = Assert.Single(pending2.Data!.Applications);
        var (_, accepted) = await leader.Post<object?>($"/api/alliances/applications/{app2.Id}/accept");
        Assert.Equal(0, accepted.Code);

        var extra = new ApiClient(_factory.CreateJsonClient());
        var e = await extra.RegisterPlayerAsync("ex");
        await extra.Post<object?>($"/api/alliances/{created.Data.Id}/apply");
        var (_, pending3) = await leader.Get<AlliancePendingDto>("/api/alliances/pending");
        var app3 = Assert.Single(pending3.Data!.Applications);
        await leader.Post<object?>($"/api/alliances/applications/{app3.Id}/accept");

        var (_, kicked) = await leader.Post<object?>("/api/alliances/kick", new { characterId = e.CharacterId });
        Assert.Equal(0, kicked.Code);
        var (_, extraMe) = await extra.Get<AllianceDetailDto>("/api/alliances/me");
        Assert.Equal(ErrorCodes.NotInAlliance, extraMe.Code);
        var (_, extraMail) = await extra.Get<MailListDto>("/api/mail");
        Assert.Contains(extraMail.Data!.Items, mail => mail.Body.Contains("移出"));

        var (_, afterKick) = await leader.Get<AllianceDetailDto>("/api/alliances/me");
        Assert.Equal(2, afterKick.Data?.MemberCount);

        var (_, memberLeave) = await member.Post<object?>("/api/alliances/leave");
        Assert.Equal(0, memberLeave.Code);
        var (_, afterLeave) = await leader.Get<AllianceDetailDto>("/api/alliances/me");
        Assert.Equal(1, afterLeave.Data?.MemberCount);
        Assert.Equal(AllianceRole.Leader, afterLeave.Data?.MyRole);

        var (_, dissolved) = await leader.Post<object?>("/api/alliances/dissolve");
        Assert.Equal(0, dissolved.Code);
        var (_, gone) = await leader.Get<AllianceDetailDto>("/api/alliances/me");
        Assert.Equal(ErrorCodes.NotInAlliance, gone.Code);
        var (_, listAfter) = await leader.Get<PagedResult<AllianceSummaryDto>>("/api/alliances?page=1&pageSize=100");
        Assert.DoesNotContain(listAfter.Data!.Items, a => a.Id == created.Data.Id);
    }

    [SkippableFact]
    public async Task Alliance_SoleLeaderLeave_Dissolves()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        await api.RegisterPlayerAsync("sl");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var (_, created) = await api.Post<AllianceDetailDto>("/api/alliances", new { name = "独" + tag });
        Assert.Equal(0, created.Code);
        var (_, left) = await api.Post<object?>("/api/alliances/leave");
        Assert.Equal(0, left.Code);
        var (_, me) = await api.Get<AllianceDetailDto>("/api/alliances/me");
        Assert.Equal(ErrorCodes.NotInAlliance, me.Code);
    }

    [SkippableFact]
    public async Task Pagination_InvalidPage_Is40001()
    {
        SkipIfUnavailable();
        var api = new ApiClient(_factory.CreateJsonClient());
        await api.RegisterPlayerAsync();
        var (_, bad) = await api.Get<PagedResult<AllianceSummaryDto>>("/api/alliances?page=0&pageSize=20");
        Assert.Equal(ErrorCodes.ValidationFailed, bad.Code);
        var (_, mailBad) = await api.Get<MailListDto>("/api/mail?page=1&pageSize=0");
        Assert.Equal(ErrorCodes.ValidationFailed, mailBad.Code);
    }

    private async Task UpgradeAndFinish(ApiClient api, string buildingType)
    {
        var (_, body) = await api.Post<BuildingsOverviewDto>("/api/buildings/upgrade", new { buildingType });
        Assert.Equal(0, body.Code);
        await _factory.ForceCompleteBuildingsAsync();
    }

    private void SkipIfUnavailable() =>
        Skip.If(!_factory.Available, _factory.UnavailableReason ?? "需要 PostgreSQL 或 Docker");
}
