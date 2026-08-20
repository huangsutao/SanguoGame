<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import {
  collectFields,
  createCharacter,
  fetchArmy,
  fetchBuildings,
  fetchFields,
  fetchReports,
  fetchSession,
  fetchWalls,
  fetchWorld,
  foundCity,
  login,
  logout,
  march,
  register,
  recruit,
  upgradeBuilding,
  upgradeField,
  upgradeWall,
  fetchMail,
  readMail,
  readAllMail,
  fetchRankings,
  fetchAlliances,
  fetchMyAlliance,
  fetchAlliancePending,
  createAlliance,
  applyAlliance,
  inviteAlliance,
  acceptAllianceInvite,
  declineAllianceInvite,
  acceptAllianceApplication,
  rejectAllianceApplication,
  leaveAlliance,
  dissolveAlliance,
  kickAllianceMember,
  updateAllianceNotice,
  fetchMarkets,
  tradeMarket,
  aidMarket
} from "./api/game";
import { createGameHub } from "./api/hub";
import { ApiError } from "./api/types";
import type {
  ArmyOverviewDto,
  BuildingsOverviewDto,
  FieldsOverviewDto,
  MarchTarget,
  PagedResult,
  BattleReportDto,
  SessionResponse,
  WallsOverviewDto,
  WorldDto,
  MailListDto,
  RankingDto,
  RankingType,
  AllianceDetailDto,
  AlliancePendingDto,
  AllianceSummaryDto,
  BuildingCostDto,
  MarketsOverviewDto
} from "./api/types";
import { clearTokens, getAccessToken, getRefreshToken, saveTokens, setUnauthorizedHandler } from "./session";
import type { HubConnection } from "@microsoft/signalr";
import WorldMap from "./WorldMap.vue";
import { buildingPortrait, resourceArt, resourceKeys, troopPortrait } from "./art";

const loading = ref(true);
const busy = ref(false);
const error = ref("");
const notice = ref("");
const mode = ref<"login" | "register">("login");
const tab = ref<"city" | "army" | "map" | "reports" | "mail" | "ranks" | "alliance" | "market">("city");
const session = ref<SessionResponse | null>(null);
const overview = ref<BuildingsOverviewDto | null>(null);
const fields = ref<FieldsOverviewDto | null>(null);
const walls = ref<WallsOverviewDto | null>(null);
const army = ref<ArmyOverviewDto | null>(null);
const world = ref<WorldDto | null>(null);
const reports = ref<PagedResult<BattleReportDto> | null>(null);
const reportsPage = ref(1);
const mail = ref<MailListDto | null>(null);
const ranking = ref<RankingDto | null>(null);
const rankingType = ref<RankingType>("power");
const alliance = ref<AllianceDetailDto | null>(null);
const allianceList = ref<PagedResult<AllianceSummaryDto> | null>(null);
const alliancePending = ref<AlliancePendingDto | null>(null);
const allianceName = ref("");
const inviteName = ref("");
const allianceNoticeDraft = ref("");
const selected = ref<MarchTarget | null>(null);
const nowMs = ref(Date.now());
const recruitType = ref("infantry");
const recruitCount = ref(10);
const marchInf = ref(20);
const marchArc = ref(0);
const marchCav = ref(0);
const markets = ref<MarketsOverviewDto | null>(null);
const selectedMarketId = ref<number | null>(null);
const tradeFrom = ref("grain");
const tradeTo = ref("wood");
const tradeAmount = ref(1000);
const aidCityId = ref<number | null>(null);
const aidGrain = ref(200);
const aidWood = ref(0);
const aidIron = ref(0);
const aidCopper = ref(0);

const username = ref("");
const password = ref("");
const characterName = ref("");

const loggedIn = computed(() => session.value !== null);
const hasCharacter = computed(() => Boolean(session.value?.character));
const hasCity = computed(() => Boolean(session.value?.city));
const queue = computed(() => overview.value?.queue ?? fields.value?.queue ?? walls.value?.queue);
const hudResources = computed(
  () =>
    overview.value?.resources ??
    fields.value?.resources ??
    walls.value?.resources ??
    army.value?.resources ??
    markets.value?.resources ??
    null
);
const hudCap = computed(
  () =>
    overview.value?.resourceCap ??
    fields.value?.resourceCap ??
    walls.value?.resourceCap ??
    army.value?.resourceCap ??
    markets.value?.resourceCap ??
    0
);

function costParts(next?: BuildingCostDto): { key: (typeof resourceKeys)[number]; amount: number }[] {
  if (!next) {
    return [];
  }
  return resourceKeys
    .map((key) => ({ key, amount: next.cost[key] }))
    .filter((item) => item.amount > 0);
}

function levelWidth(level: number, maxLevel: number): string {
  if (maxLevel <= 0) {
    return "0%";
  }
  return `${Math.max(0, Math.min(100, (level / maxLevel) * 100))}%`;
}

const selectedTroop = computed(() =>
  army.value?.troopTypes?.find((item) => item.type === recruitType.value)
);

const selectedMarket = computed(() =>
  markets.value?.markets.find((item) => item.id === selectedMarketId.value) ?? null
);

const tradePreview = computed(() => {
  const amount = Math.max(0, Number(tradeAmount.value) || 0);
  const rate = markets.value?.rates.find(
    (item) => item.fromResource === tradeFrom.value && item.toResource === tradeTo.value
  );
  if (!rate || amount <= 0) {
    return "";
  }
  const got = Math.floor((amount * rate.toAmount) / rate.fromAmount);
  const trip = selectedMarket.value ? `${selectedMarket.value.roundTripSeconds}秒往返` : "先选市集";
  return `付出 ${resourceLabel[tradeFrom.value]}${amount} → 换得 ${resourceLabel[tradeTo.value]}${got}（${trip}）`;
});

const aidMembers = computed(() =>
  (alliance.value?.members ?? []).filter(
    (item) => item.characterId !== session.value?.character?.id && item.cityId > 0
  )
);

const resourceLabel: Record<string, string> = {
  grain: "粮",
  wood: "木",
  iron: "铁",
  copper: "铜"
};

const troopLabel: Record<string, string> = {
  infantry: "步兵",
  archer: "弓兵",
  cavalry: "骑兵"
};

const allianceRoleLabel: Record<string, string> = {
  leader: "盟主",
  officer: "官员",
  member: "成员"
};

let hub: HubConnection | null = null;
let tick: number | undefined;
let lastWorldRefresh = 0;

function fail(err: unknown): void {
  error.value = err instanceof ApiError || err instanceof Error ? err.message : "操作失败";
}

function queueName(type?: string): string {
  if (!type) {
    return "";
  }
  return (
    overview.value?.buildings.find((item) => item.type === type)?.name ??
    fields.value?.fields.find((item) => item.type === type)?.name ??
    walls.value?.walls.find((item) => item.type === type)?.name ??
    type
  );
}

function remainText(finishAt?: string): string {
  if (!finishAt) {
    return "";
  }
  const ms = Date.parse(finishAt) - nowMs.value;
  if (ms <= 0) {
    return "即将完成";
  }
  const sec = Math.ceil(ms / 1000);
  const m = Math.floor(sec / 60);
  const s = sec % 60;
  return m > 0 ? `${m}分${s}秒` : `${s}秒`;
}

function blockedText(reason?: string): string {
  switch (reason) {
    case "queue":
      return "队列占用中";
    case "maxLevel":
      return "已满级";
    case "prerequisite":
      return "前置未满足";
    case "resources":
      return "资源不足";
    default:
      return "";
  }
}

function effectsText(effects?: Record<string, number>): string {
  if (!effects) {
    return "";
  }
  const labels: Record<string, [string, "percent" | "flat"]> = {
    populationCap: ["人口上限", "flat"],
    resourceCap: ["仓库上限", "flat"],
    attackBonusPercent: ["攻方战力", "percent"],
    troopPowerBonusPercent: ["兵力战力", "percent"],
    recruitDiscountPercent: ["征兵减免", "percent"],
    wallDefenseFlat: ["城防", "flat"],
    trapBonusPercent: ["陷阱", "percent"],
    productionBonusPercent: ["田产出", "percent"],
    troopCap: ["带兵上限", "flat"],
    wallDefense: ["城防", "flat"],
    trapBonus: ["陷阱", "percent"]
  };
  return Object.entries(effects)
    .map(([key, value]) => {
      const [name, kind] = labels[key] ?? [key, "flat"];
      return kind === "percent" ? `${name}+${value}%` : `${name}+${value}`;
    })
    .join(" · ");
}

function protectionText(until?: string): string {
  if (!until) {
    return "";
  }
  const ms = Date.parse(until) - nowMs.value;
  if (ms <= 0) {
    return "";
  }
  return `保护中 ${remainText(until)}`;
}

async function loadCity(): Promise<void> {
  const [inner, outer, wall] = await Promise.all([fetchBuildings(), fetchFields(), fetchWalls()]);
  overview.value = inner;
  fields.value = outer;
  walls.value = wall;
}

async function loadArmy(): Promise<void> {
  army.value = await fetchArmy();
}

async function loadWorld(): Promise<void> {
  world.value = await fetchWorld();
}

async function loadMarkets(): Promise<void> {
  markets.value = await fetchMarkets();
  if (selectedMarketId.value == null && markets.value.markets.length > 0) {
    selectedMarketId.value = markets.value.markets[0].id;
  }
}

async function loadReports(page = 1, append = false): Promise<void> {
  const data = await fetchReports(page);
  reports.value = append && reports.value
    ? { ...data, items: [...reports.value.items, ...data.items] }
    : data;
  reportsPage.value = page;
}

async function loadMoreReports(): Promise<void> {
  if (busy.value || !reports.value || reports.value.items.length >= reports.value.total) {
    return;
  }
  busy.value = true;
  try {
    await loadReports(reportsPage.value + 1, true);
  } catch (err) {
    fail(err);
  } finally {
    busy.value = false;
  }
}

async function loadMail(): Promise<void> {
  mail.value = await fetchMail();
}

async function loadRankings(): Promise<void> {
  ranking.value = await fetchRankings(rankingType.value);
}

async function loadAlliance(): Promise<void> {
  const [list, pending] = await Promise.all([fetchAlliances(), fetchAlliancePending()]);
  allianceList.value = list;
  alliancePending.value = pending;
  try {
    alliance.value = await fetchMyAlliance();
    allianceNoticeDraft.value = alliance.value.notice ?? "";
  } catch (err) {
    if (err instanceof ApiError && err.code === 40922) {
      alliance.value = null;
      allianceNoticeDraft.value = "";
      return;
    }
    throw err;
  }
}

async function loadAll(): Promise<void> {
  await Promise.all([
    loadCity(),
    loadArmy(),
    loadWorld(),
    loadReports(),
    loadMail(),
    loadRankings(),
    loadAlliance(),
    loadMarkets()
  ]);
}

async function connectHub(): Promise<void> {
  await disconnectHub();
  hub = createGameHub();
  hub.on("BuildComplete", () => {
    void loadCity();
  });
  hub.on("MarchArrived", () => {
    void loadAll();
  });
  hub.on("CityAttacked", () => {
    void loadAll();
    notice.value = "本城遭到攻击";
  });
  hub.on("TransportArrived", () => {
    void loadAll();
    notice.value = "运输已到达";
  });
  hub.on("ResourceReceived", () => {
    void loadAll();
    notice.value = "收到同盟资源";
  });
  hub.onreconnected(() => {
    void loadAll();
  });
  await hub.start();
}

async function disconnectHub(): Promise<void> {
  if (hub) {
    hub.off("BuildComplete");
    hub.off("MarchArrived");
    hub.off("CityAttacked");
    hub.off("TransportArrived");
    hub.off("ResourceReceived");
    await hub.stop();
    hub = null;
  }
}

onMounted(async () => {
  setUnauthorizedHandler(() => {
    session.value = null;
    overview.value = null;
    fields.value = null;
    walls.value = null;
    army.value = null;
    world.value = null;
    reports.value = null;
    mail.value = null;
    ranking.value = null;
    alliance.value = null;
    allianceList.value = null;
    alliancePending.value = null;
    markets.value = null;
    void disconnectHub();
  });

  tick = window.setInterval(() => {
    nowMs.value = Date.now();
    if (tab.value === "map" && hasCity.value && Date.now() - lastWorldRefresh >= 15000) {
      lastWorldRefresh = Date.now();
      void loadWorld().catch(() => undefined);
    }
  }, 1000);

  if (!getAccessToken()) {
    loading.value = false;
    return;
  }

  try {
    session.value = await fetchSession();
  } catch {
    clearTokens();
    session.value = null;
  } finally {
    loading.value = false;
  }
});

onUnmounted(() => {
  setUnauthorizedHandler(null);
  if (tick !== undefined) {
    window.clearInterval(tick);
  }
  void disconnectHub();
});

watch(hasCity, async (ready) => {
  if (!ready) {
    overview.value = null;
    fields.value = null;
    walls.value = null;
    army.value = null;
    world.value = null;
    reports.value = null;
    mail.value = null;
    ranking.value = null;
    alliance.value = null;
    allianceList.value = null;
    alliancePending.value = null;
    markets.value = null;
    await disconnectHub();
    return;
  }
  try {
    await loadAll();
    await connectHub();
  } catch (err) {
    fail(err);
  }
});

watch(tab, (value) => {
  if (value === "map" && hasCity.value) {
    lastWorldRefresh = Date.now();
    void loadWorld().catch(fail);
  }
});

async function submitAuth(): Promise<void> {
  error.value = "";
  notice.value = "";
  busy.value = true;
  try {
    const tokens =
      mode.value === "register"
        ? await register(username.value.trim(), password.value)
        : await login(username.value.trim(), password.value);
    saveTokens(tokens.accessToken, tokens.refreshToken, tokens.expiresAt);
    session.value = await fetchSession();
    password.value = "";
  } catch (err) {
    fail(err);
  } finally {
    busy.value = false;
  }
}

async function submitCharacter(): Promise<void> {
  error.value = "";
  busy.value = true;
  try {
    const character = await createCharacter(characterName.value.trim());
    if (session.value) {
      session.value = { ...session.value, character: { id: character.id, name: character.name } };
    }
  } catch (err) {
    fail(err);
  } finally {
    busy.value = false;
  }
}

async function submitFoundCity(): Promise<void> {
  error.value = "";
  busy.value = true;
  try {
    const city = await foundCity();
    if (session.value) {
      session.value = {
        ...session.value,
        city: { id: city.id, name: city.name, x: city.x, y: city.y }
      };
    }
  } catch (err) {
    fail(err);
  } finally {
    busy.value = false;
  }
}

async function submitUpgrade(type: string): Promise<void> {
  error.value = "";
  notice.value = "";
  busy.value = true;
  try {
    overview.value = await upgradeBuilding(type);
    await Promise.all([loadCity()]);
  } catch (err) {
    fail(err);
  } finally {
    busy.value = false;
  }
}

async function submitFieldUpgrade(type: string): Promise<void> {
  error.value = "";
  notice.value = "";
  busy.value = true;
  try {
    fields.value = await upgradeField(type);
    await loadCity();
  } catch (err) {
    fail(err);
  } finally {
    busy.value = false;
  }
}

async function submitWallUpgrade(type: string): Promise<void> {
  error.value = "";
  notice.value = "";
  busy.value = true;
  try {
    walls.value = await upgradeWall(type);
    await loadCity();
  } catch (err) {
    fail(err);
  } finally {
    busy.value = false;
  }
}

async function submitCollect(type?: string): Promise<void> {
  error.value = "";
  notice.value = "";
  busy.value = true;
  try {
    const result = await collectFields(type);
    fields.value = {
      cityId: result.data.cityId,
      serverTime: result.data.serverTime,
      resources: result.data.resources,
      resourceCap: result.data.resourceCap,
      queue: fields.value?.queue,
      fields: result.data.fields
    };
    if (overview.value) {
      overview.value = { ...overview.value, resources: result.data.resources, resourceCap: result.data.resourceCap };
    }
    if (walls.value) {
      walls.value = { ...walls.value, resources: result.data.resources, resourceCap: result.data.resourceCap };
    }
    if (result.message && result.message !== "ok") {
      notice.value = result.message;
    }
  } catch (err) {
    fail(err);
  } finally {
    busy.value = false;
  }
}

async function submitRecruit(): Promise<void> {
  error.value = "";
  notice.value = "";
  if (!Number.isFinite(recruitCount.value) || recruitCount.value < 1 || recruitCount.value > 100) {
    error.value = "征兵数量为 1～100";
    return;
  }
  busy.value = true;
  try {
    army.value = await recruit(recruitType.value, recruitCount.value);
    overview.value = await fetchBuildings();
  } catch (err) {
    fail(err);
  } finally {
    busy.value = false;
  }
}

async function submitMarch(): Promise<void> {
  if (!selected.value) {
    error.value = "请先在地图上点选目标";
    return;
  }
  const infantry = Number.isFinite(marchInf.value) ? Math.max(0, marchInf.value) : 0;
  const archer = Number.isFinite(marchArc.value) ? Math.max(0, marchArc.value) : 0;
  const cavalry = Number.isFinite(marchCav.value) ? Math.max(0, marchCav.value) : 0;
  if (infantry + archer + cavalry <= 0) {
    error.value = "出征至少需要 1 名士兵";
    return;
  }
  error.value = "";
  notice.value = "";
  busy.value = true;
  try {
    army.value = await march(
      selected.value.targetType,
      selected.value.targetId,
      infantry,
      archer,
      cavalry
    );
    await Promise.all([loadWorld(), loadReports(), loadCity()]);
    notice.value = "已出征";
  } catch (err) {
    fail(err);
  } finally {
    busy.value = false;
  }
}

function onSelectTarget(target: MarchTarget): void {
  if (target.targetType === "market") {
    selectedMarketId.value = target.targetId;
    tab.value = "market";
    notice.value = `已选择 ${target.label}`;
    return;
  }
  selected.value = target;
  tab.value = "army";
  notice.value = `已选择 ${target.label}`;
}

async function submitTrade(): Promise<void> {
  if (selectedMarketId.value == null) {
    error.value = "请先选择市集";
    return;
  }
  await run(async () => {
    markets.value = await tradeMarket(
      selectedMarketId.value!,
      tradeFrom.value,
      tradeTo.value,
      Math.max(0, Number(tradeAmount.value) || 0)
    );
    await Promise.all([loadCity(), loadWorld(), loadMail()]);
    notice.value = "已出发前往市集";
  });
}

async function submitAid(): Promise<void> {
  if (aidCityId.value == null) {
    error.value = "请选择同盟成员";
    return;
  }
  await run(async () => {
    markets.value = await aidMarket(
      aidCityId.value!,
      Math.max(0, Number(aidGrain.value) || 0),
      Math.max(0, Number(aidWood.value) || 0),
      Math.max(0, Number(aidIron.value) || 0),
      Math.max(0, Number(aidCopper.value) || 0)
    );
    await Promise.all([loadCity(), loadWorld(), loadMail(), loadAlliance()]);
    notice.value = "资源运输已出发";
  });
}

async function run(action: () => Promise<void>): Promise<void> {
  error.value = "";
  notice.value = "";
  busy.value = true;
  try {
    await action();
  } catch (err) {
    fail(err);
  } finally {
    busy.value = false;
  }
}

async function submitReadMail(id: number): Promise<void> {
  await run(async () => {
    await readMail(id);
    await loadMail();
  });
}

async function submitReadAllMail(): Promise<void> {
  await run(async () => {
    await readAllMail();
    await loadMail();
  });
}

async function changeRanking(type: RankingType): Promise<void> {
  rankingType.value = type;
  await run(async () => {
    await loadRankings();
  });
}

async function submitCreateAlliance(): Promise<void> {
  await run(async () => {
    alliance.value = await createAlliance(allianceName.value.trim());
    allianceName.value = "";
    await loadAlliance();
    notice.value = "联盟已创建";
  });
}

async function submitApplyAlliance(id: number): Promise<void> {
  await run(async () => {
    await applyAlliance(id);
    await loadAlliance();
    notice.value = "已发出申请";
  });
}

async function submitInvite(): Promise<void> {
  await run(async () => {
    await inviteAlliance(inviteName.value.trim());
    inviteName.value = "";
    await loadAlliance();
    notice.value = "已发出邀请";
  });
}

async function submitAcceptInvite(id: number): Promise<void> {
  await run(async () => {
    await acceptAllianceInvite(id);
    await loadAlliance();
  });
}

async function submitDeclineInvite(id: number): Promise<void> {
  await run(async () => {
    await declineAllianceInvite(id);
    await loadAlliance();
  });
}

async function submitAcceptApplication(id: number): Promise<void> {
  await run(async () => {
    await acceptAllianceApplication(id);
    await loadAlliance();
  });
}

async function submitRejectApplication(id: number): Promise<void> {
  await run(async () => {
    await rejectAllianceApplication(id);
    await loadAlliance();
  });
}

async function submitLeaveAlliance(): Promise<void> {
  await run(async () => {
    await leaveAlliance();
    await loadAlliance();
  });
}

async function submitDissolveAlliance(): Promise<void> {
  await run(async () => {
    await dissolveAlliance();
    await loadAlliance();
  });
}

async function submitKick(characterId: number): Promise<void> {
  await run(async () => {
    await kickAllianceMember(characterId);
    await loadAlliance();
    notice.value = "已移出该成员";
  });
}

async function submitAllianceNotice(): Promise<void> {
  await run(async () => {
    await updateAllianceNotice(allianceNoticeDraft.value.trim());
    await loadAlliance();
    notice.value = "公告已更新";
  });
}

async function submitLogout(): Promise<void> {
  error.value = "";
  const refresh = getRefreshToken();
  try {
    if (refresh) {
      await logout(refresh);
    }
  } catch {
    // 本地仍退出
  } finally {
    await disconnectHub();
    clearTokens();
    session.value = null;
    overview.value = null;
    fields.value = null;
    walls.value = null;
    army.value = null;
    world.value = null;
    reports.value = null;
    mail.value = null;
    ranking.value = null;
    alliance.value = null;
    allianceList.value = null;
    alliancePending.value = null;
    password.value = "";
  }
}
</script>

<template>
  <main class="page" :class="{ wide: hasCity }">
    <header v-if="!hasCity" class="header splash">
      <img class="splash-art" src="/art/palace.jpg" alt="" />
      <h1>战国</h1>
      <p class="sub">建城 · 内政 · 出征 · 地图 · 联盟</p>
    </header>
    <header v-else class="hud-top">
      <div class="brand">
        <img class="brand-seal" src="/art/palace.jpg" alt="" />
        <div>
          <h1>战国</h1>
          <p class="city-name">
            {{ session?.city?.name }} · ({{ session?.city?.x }}, {{ session?.city?.y }})
            <span v-if="protectionText(army?.protectionUntil)" class="protect">
              {{ protectionText(army?.protectionUntil) }}
            </span>
          </p>
        </div>
      </div>
      <div v-if="hudResources" class="res-bar">
        <span v-for="key in resourceKeys" :key="key" class="res-chip">
          <img :src="resourceArt[key]" :alt="resourceLabel[key]" />
          <b>{{ hudResources[key] }}</b>
          <small>{{ resourceLabel[key] }}</small>
        </span>
      </div>
      <div class="who">
        <span>{{ session?.username }}</span>
        <button type="button" class="link" @click="submitLogout">退出</button>
      </div>
    </header>
    <p v-if="hasCity && hudResources" class="hint cap-line">仓库上限 {{ hudCap }}<template v-if="overview"> · 人口上限 {{ overview.populationCap }}</template></p>

    <p v-if="loading" class="hint">加载中…</p>

    <section v-else class="card">
      <p v-if="error" class="error">{{ error }}</p>
      <p v-else-if="notice" class="hint">{{ notice }}</p>

      <form v-if="!loggedIn" class="form" @submit.prevent="submitAuth">
        <div class="tabs">
          <button type="button" :class="{ active: mode === 'login' }" @click="mode = 'login'">登录</button>
          <button type="button" :class="{ active: mode === 'register' }" @click="mode = 'register'">注册</button>
        </div>
        <label>
          用户名
          <input v-model="username" autocomplete="username" maxlength="16" placeholder="3～16 位字母数字下划线" />
        </label>
        <label>
          密码
          <input
            v-model="password"
            type="password"
            autocomplete="current-password"
            maxlength="64"
            placeholder="至少 8 位"
          />
        </label>
        <button type="submit" :disabled="busy">{{ mode === "register" ? "注册并进入" : "登录" }}</button>
      </form>

      <div v-else class="play">
        <div v-if="!hasCity" class="who">
          <span>账号 {{ session?.username }}</span>
          <button type="button" class="link" @click="submitLogout">退出</button>
        </div>

        <section v-if="!hasCharacter" class="block">
          <h2>创建角色</h2>
          <form class="form" @submit.prevent="submitCharacter">
            <label>
              角色名
              <input v-model="characterName" maxlength="12" placeholder="2～12 位，可用中文" />
            </label>
            <button type="submit" :disabled="busy">创建角色</button>
          </form>
        </section>

        <section v-else-if="!hasCity" class="block">
          <h2>{{ session?.character?.name }}</h2>
          <p class="hint">坐标由服务端在地图空地随机选取，客户端不传位置。</p>
          <button type="button" :disabled="busy" @click="submitFoundCity">建城</button>
        </section>

        <template v-else>
          <div class="tabs play">
            <button type="button" :class="{ active: tab === 'city' }" @click="tab = 'city'">城池</button>
            <button type="button" :class="{ active: tab === 'army' }" @click="tab = 'army'">军队</button>
            <button type="button" :class="{ active: tab === 'map' }" @click="tab = 'map'">地图</button>
            <button type="button" :class="{ active: tab === 'market' }" @click="tab = 'market'">市集</button>
            <button type="button" :class="{ active: tab === 'reports' }" @click="tab = 'reports'">战报</button>
            <button type="button" :class="{ active: tab === 'mail' }" @click="tab = 'mail'">
              邮件{{ mail?.unreadCount ? ` ${mail.unreadCount}` : "" }}
            </button>
            <button type="button" :class="{ active: tab === 'ranks' }" @click="tab = 'ranks'">排行</button>
            <button type="button" :class="{ active: tab === 'alliance' }" @click="tab = 'alliance'">联盟</button>
          </div>

          <section v-if="tab === 'city' && overview" class="block">
            <h2>城内</h2>
            <p v-if="queue" class="queue-banner">
              建造中：{{ queueName(queue.buildingType) }} → {{ queue.targetLevel }} 级，剩余
              {{ remainText(queue.finishAt) }}
            </p>
            <ul class="cards">
              <li
                v-for="item in overview.buildings"
                :key="item.type"
                class="portrait-card"
                :class="{ locked: Boolean(item.blockedReason), upgrading: item.status === 'upgrading' }"
              >
                <div class="portrait">
                  <img :src="buildingPortrait(item.type)" :alt="item.name" />
                  <span class="lv">{{ item.level }}/{{ item.maxLevel }}</span>
                </div>
                <div class="info">
                  <strong>{{ item.name }}</strong>
                  <div class="level-bar"><i :style="{ width: levelWidth(item.level, item.maxLevel) }"></i></div>
                  <span v-if="effectsText(item.effects)" class="hint">{{ effectsText(item.effects) }}</span>
                  <span v-if="item.status === 'upgrading'" class="hint">
                    升级中 {{ remainText(item.finishAt) }}
                  </span>
                  <span v-else-if="blockedText(item.blockedReason)" class="hint">{{
                    blockedText(item.blockedReason)
                  }}</span>
                  <div v-if="item.next" class="cost-row">
                    <span v-for="part in costParts(item.next)" :key="part.key" class="cost-chip">
                      <img :src="resourceArt[part.key]" :alt="resourceLabel[part.key]" />
                      {{ part.amount }}
                    </span>
                    <span class="cost-chip time">{{ item.next.durationSeconds }}秒</span>
                  </div>
                </div>
                <div class="card-actions">
                  <button
                    type="button"
                    :disabled="busy || item.status === 'upgrading' || Boolean(item.blockedReason)"
                    @click="submitUpgrade(item.type)"
                  >
                    {{ item.level === 0 ? "建造" : "升级" }}
                  </button>
                </div>
              </li>
            </ul>
          </section>

          <section v-if="tab === 'city' && fields" class="block">
            <h2>城外</h2>
            <p class="hint">主殿生效 1 级后可建；产出按上次收取时间现算，点收取才入库。</p>
            <p>
              <button type="button" :disabled="busy" @click="submitCollect()">一键收取</button>
            </p>
            <ul class="cards">
              <li
                v-for="item in fields.fields"
                :key="item.type"
                class="portrait-card"
                :class="{ locked: Boolean(item.blockedReason), upgrading: item.status === 'upgrading' }"
              >
                <div class="portrait">
                  <img :src="buildingPortrait(item.type)" :alt="item.name" />
                  <span class="lv">{{ item.level }}/{{ item.maxLevel }}</span>
                </div>
                <div class="info">
                  <strong>{{ item.name }}</strong>
                  <div class="level-bar"><i :style="{ width: levelWidth(item.level, item.maxLevel) }"></i></div>
                  <span v-if="item.level >= 1" class="hint">{{ item.ratePerHour }}/时</span>
                  <span v-if="item.pending > 0" class="pending-chip">
                    可收 {{ item.pending }} / {{ item.fieldCap }} {{ resourceLabel[item.resource] }}
                  </span>
                  <span v-if="item.status === 'upgrading'" class="hint">
                    升级中 {{ remainText(item.finishAt) }}
                  </span>
                  <span v-else-if="blockedText(item.blockedReason)" class="hint">{{
                    blockedText(item.blockedReason)
                  }}</span>
                  <div v-if="item.next" class="cost-row">
                    <span v-for="part in costParts(item.next)" :key="part.key" class="cost-chip">
                      <img :src="resourceArt[part.key]" :alt="resourceLabel[part.key]" />
                      {{ part.amount }}
                    </span>
                    <span class="cost-chip time">{{ item.next.durationSeconds }}秒</span>
                  </div>
                </div>
                <div class="card-actions">
                  <button type="button" :disabled="busy || item.level < 1" @click="submitCollect(item.type)">
                    收取
                  </button>
                  <button
                    type="button"
                    :disabled="busy || item.status === 'upgrading' || Boolean(item.blockedReason)"
                    @click="submitFieldUpgrade(item.type)"
                  >
                    {{ item.level === 0 ? "建造" : "升级" }}
                  </button>
                </div>
              </li>
            </ul>
          </section>

          <section v-if="tab === 'city' && walls" class="block">
            <h2>城墙</h2>
            <p class="res">守城 {{ walls.wallDefense }} · 陷阱加成 {{ Math.round(walls.trapBonus * 100) }}%</p>
            <p class="hint">主殿 2 级可建箭塔 / 城门，3 级可建陷阱。与城内、城外共用一条建造队列。</p>
            <ul class="cards">
              <li
                v-for="item in walls.walls"
                :key="item.type"
                class="portrait-card"
                :class="{ locked: Boolean(item.blockedReason), upgrading: item.status === 'upgrading' }"
              >
                <div class="portrait">
                  <img :src="buildingPortrait(item.type)" :alt="item.name" />
                  <span class="lv">{{ item.level }}/{{ item.maxLevel }}</span>
                </div>
                <div class="info">
                  <strong>{{ item.name }}</strong>
                  <div class="level-bar"><i :style="{ width: levelWidth(item.level, item.maxLevel) }"></i></div>
                  <span v-if="effectsText(item.effects)" class="hint">{{ effectsText(item.effects) }}</span>
                  <span v-if="item.status === 'upgrading'" class="hint">
                    升级中 {{ remainText(item.finishAt) }}
                  </span>
                  <span v-else-if="blockedText(item.blockedReason)" class="hint">{{
                    blockedText(item.blockedReason)
                  }}</span>
                  <div v-if="item.next" class="cost-row">
                    <span v-for="part in costParts(item.next)" :key="part.key" class="cost-chip">
                      <img :src="resourceArt[part.key]" :alt="resourceLabel[part.key]" />
                      {{ part.amount }}
                    </span>
                    <span class="cost-chip time">{{ item.next.durationSeconds }}秒</span>
                  </div>
                </div>
                <div class="card-actions">
                  <button
                    type="button"
                    :disabled="busy || item.status === 'upgrading' || Boolean(item.blockedReason)"
                    @click="submitWallUpgrade(item.type)"
                  >
                    {{ item.level === 0 ? "建造" : "升级" }}
                  </button>
                </div>
              </li>
            </ul>
          </section>

          <section v-if="tab === 'army' && army" class="block">
            <h2>军队</h2>
            <div class="troop-row">
              <div class="troop-card">
                <img :src="troopPortrait('infantry')" alt="步兵" />
                <strong>步兵</strong>
                <b>{{ army.troops.infantry }}</b>
              </div>
              <div class="troop-card">
                <img :src="troopPortrait('archer')" alt="弓兵" />
                <strong>弓兵</strong>
                <b>{{ army.troops.archer }}</b>
              </div>
              <div class="troop-card">
                <img :src="troopPortrait('cavalry')" alt="骑兵" />
                <strong>骑兵</strong>
                <b>{{ army.troops.cavalry }}</b>
              </div>
            </div>
            <p class="hint">
              带兵上限 {{ army.troopCap }} · 兵营 {{ army.barracksLevel }} 级 · 城防 {{ army.wallDefense }}
              <template v-if="army.troopPowerBonusPercent"> · 兵力战力+{{ army.troopPowerBonusPercent }}%</template>
            </p>
            <div class="form inline">
              <label>
                兵种
                <select v-model="recruitType">
                  <option value="infantry">步兵</option>
                  <option value="archer">弓兵</option>
                  <option value="cavalry">骑兵</option>
                </select>
              </label>
              <label>
                数量
                <input v-model.number="recruitCount" type="number" min="1" max="100" />
              </label>
              <button type="button" :disabled="busy" @click="submitRecruit">征兵</button>
            </div>
            <div v-if="selectedTroop" class="cost-row">
              <span v-for="key in resourceKeys" :key="key" class="cost-chip">
                <img :src="resourceArt[key]" :alt="resourceLabel[key]" />
                {{ selectedTroop.unitCost[key] * Math.max(0, Number(recruitCount) || 0) }}
              </span>
              <span class="cost-chip time">兵营 ≥ {{ selectedTroop.requireBarracksLevel }}</span>
              <span v-if="army.recruitDiscountPercent" class="hint">征兵减免 {{ army.recruitDiscountPercent }}%</span>
            </div>
            <p v-else class="hint">步兵需兵营 1 级，弓兵 2 级，骑兵 3 级。征兵即时扣资源。</p>
            <h3>出征</h3>
            <p class="hint">{{ selected ? `目标：${selected.label}` : "在地图点选据点或其他玩家城" }}</p>
            <div class="form inline">
              <label>步 <input v-model.number="marchInf" type="number" min="0" /></label>
              <label>弓 <input v-model.number="marchArc" type="number" min="0" /></label>
              <label>骑 <input v-model.number="marchCav" type="number" min="0" /></label>
              <button type="button" :disabled="busy || !selected" @click="submitMarch">出征</button>
            </div>
            <ul class="buildings">
              <li v-for="item in army.marches" :key="item.id">
                <div>
                  <strong>行军 #{{ item.id }}</strong>
                  <span class="meta">{{ item.fromX }},{{ item.fromY }} → {{ item.toX }},{{ item.toY }}</span>
                  <span class="hint">到达 {{ remainText(item.arriveAt) }}</span>
                </div>
              </li>
            </ul>
          </section>

          <section v-if="tab === 'map' && world" class="block">
            <h2>大地图</h2>
            <div class="legend">
              <span><img src="/art/marker-city.jpg" alt="" />城池（金圈自己 / 红圈 AI / 绿圈玩家）</span>
              <span><img src="/art/marker-outpost.jpg" alt="" />常驻据点</span>
              <span><img src="/art/marker-roaming.jpg" alt="" />限时流寇</span>
              <span><img src="/art/marker-market.jpg" alt="" />市集</span>
              <span><img src="/art/marker-march.jpg" alt="" />行军（沿线走动）</span>
              <span><img src="/art/marker-cart.jpg" alt="" />运输马车</span>
            </div>
            <p class="hint">拖拽移动，滚轮缩放。行军旗和运输车会沿线走动。点击据点或玩家城出征，点击市集兑换。</p>
            <WorldMap :world="world" @select="onSelectTarget" />
          </section>

          <section v-if="tab === 'market' && markets" class="block">
            <h2>市集</h2>
            <p class="hint">
              单次运量 {{ markets.cargoCap }} · 税率 {{ Math.round(markets.taxRate * 100) }}% · 最少付出
              {{ markets.minAmount }}。出发立刻扣资源，往返到点后入仓。
            </p>
            <div class="form inline">
              <label>
                市集
                <select v-model.number="selectedMarketId">
                  <option v-for="item in markets.markets" :key="item.id" :value="item.id">
                    {{ item.name }}（往返 {{ item.roundTripSeconds }}秒）
                  </option>
                </select>
              </label>
              <label>
                付出
                <select v-model="tradeFrom">
                  <option value="grain">粮</option>
                  <option value="wood">木</option>
                  <option value="iron">铁</option>
                  <option value="copper">铜</option>
                </select>
              </label>
              <label>
                换得
                <select v-model="tradeTo">
                  <option value="grain">粮</option>
                  <option value="wood">木</option>
                  <option value="iron">铁</option>
                  <option value="copper">铜</option>
                </select>
              </label>
              <label>
                数量
                <input v-model.number="tradeAmount" type="number" min="100" />
              </label>
              <button type="button" :disabled="busy || !selectedMarketId" @click="submitTrade">兑换</button>
            </div>
            <p class="hint">{{ tradePreview }}</p>
            <h3>同盟运输</h3>
            <p v-if="!aidMembers.length" class="hint">加入联盟后可把资源运给其他成员，单程计时，途中退盟仍会送达。</p>
            <div v-else class="form inline">
              <label>
                成员
                <select v-model.number="aidCityId">
                  <option :value="null">选择成员</option>
                  <option v-for="item in aidMembers" :key="item.cityId" :value="item.cityId">
                    {{ item.name }}
                  </option>
                </select>
              </label>
              <label>粮 <input v-model.number="aidGrain" type="number" min="0" /></label>
              <label>木 <input v-model.number="aidWood" type="number" min="0" /></label>
              <label>铁 <input v-model.number="aidIron" type="number" min="0" /></label>
              <label>铜 <input v-model.number="aidCopper" type="number" min="0" /></label>
              <button type="button" :disabled="busy || aidCityId == null" @click="submitAid">运输</button>
            </div>
            <h3>在途</h3>
            <p v-if="!markets.transports.length" class="hint">没有在途运输</p>
            <ul class="buildings">
              <li v-for="item in markets.transports" :key="item.id">
                <div>
                  <strong>{{ item.kind === "market" ? "市集兑换" : "同盟运输" }} #{{ item.id }}</strong>
                  <span class="meta">{{ item.fromX }},{{ item.fromY }} → {{ item.toX }},{{ item.toY }}</span>
                  <span class="hint">{{ remainText(item.arriveAt) }}</span>
                  <p class="hint">
                    货 粮{{ item.cargo.grain }} 木{{ item.cargo.wood }} 铁{{ item.cargo.iron }} 铜{{ item.cargo.copper }}
                    → 入账 粮{{ item.credit.grain }} 木{{ item.credit.wood }} 铁{{ item.credit.iron }} 铜{{ item.credit.copper }}
                  </p>
                </div>
              </li>
            </ul>
          </section>

          <section v-if="tab === 'reports'" class="block">
            <h2>战报</h2>
            <p v-if="!reports?.items.length" class="hint">暂无战报</p>
            <ul class="buildings">
              <li v-for="item in reports?.items ?? []" :key="item.id">
                <div>
                  <strong :class="item.attackerWon ? 'protect' : 'lose'">{{ item.attackerWon ? "胜" : "负" }}</strong>
                  <span class="meta">{{ item.summary }}</span>
                  <p class="hint">
                    攻 {{ troopLabel.infantry }}{{ item.attackerBefore.infantry }}→{{ item.attackerAfter.infantry }} /
                    守 {{ item.defenderBefore.infantry }}→{{ item.defenderAfter.infantry }}
                  </p>
                </div>
              </li>
            </ul>
            <button
              v-if="reports && reports.items.length < reports.total"
              type="button"
              :disabled="busy"
              @click="loadMoreReports"
            >
              加载更多
            </button>
          </section>

          <section v-if="tab === 'mail'" class="block">
            <h2>邮件</h2>
            <p class="hint">未读 {{ mail?.unreadCount ?? 0 }}</p>
            <p>
              <button type="button" :disabled="busy || !mail?.unreadCount" @click="submitReadAllMail">全部已读</button>
            </p>
            <p v-if="!mail?.items.length" class="hint">暂无邮件</p>
            <ul class="buildings">
              <li v-for="item in mail?.items ?? []" :key="item.id">
                <div>
                  <strong>{{ item.isRead ? item.title : `● ${item.title}` }}</strong>
                  <p class="hint">{{ item.body }}</p>
                </div>
                <button v-if="!item.isRead" type="button" :disabled="busy" @click="submitReadMail(item.id)">已读</button>
              </li>
            </ul>
          </section>

          <section v-if="tab === 'ranks'" class="block">
            <h2>排行</h2>
            <div class="tabs">
              <button type="button" :class="{ active: rankingType === 'power' }" @click="changeRanking('power')">国力</button>
              <button type="button" :class="{ active: rankingType === 'troops' }" @click="changeRanking('troops')">兵力</button>
              <button type="button" :class="{ active: rankingType === 'loot' }" @click="changeRanking('loot')">掠夺</button>
            </div>
            <p class="hint">我的名次 {{ ranking?.myRank ?? "-" }} · 分数 {{ ranking?.myScore ?? 0 }}</p>
            <ul class="buildings">
              <li v-for="item in ranking?.items ?? []" :key="item.cityId">
                <div>
                  <strong>{{ item.rank }}. {{ item.characterName }}</strong>
                  <span class="meta">{{ item.score }}{{ item.isAi ? " · AI" : "" }}{{ item.allianceName ? ` · ${item.allianceName}` : "" }}</span>
                </div>
              </li>
            </ul>
          </section>

          <section v-if="tab === 'alliance'" class="block">
            <h2>联盟</h2>
            <template v-if="alliance">
              <p class="res">{{ alliance.name }} · {{ alliance.memberCount }} 人 · 我是 {{ allianceRoleLabel[alliance.myRole ?? "member"] }}</p>
              <p v-if="alliance.notice" class="hint">{{ alliance.notice }}</p>
              <ul class="buildings">
                <li v-for="item in alliance.members" :key="item.characterId">
                  <div>
                    <strong>{{ item.name }}</strong>
                    <span class="meta">{{ allianceRoleLabel[item.role] ?? item.role }}</span>
                  </div>
                  <button
                    v-if="
                      item.characterId !== session?.character?.id &&
                      (alliance.myRole === 'leader' || (alliance.myRole === 'officer' && item.role === 'member'))
                    "
                    type="button"
                    :disabled="busy"
                    @click="submitKick(item.characterId)"
                  >
                    踢出
                  </button>
                </li>
              </ul>
              <div v-if="alliance.myRole === 'leader' || alliance.myRole === 'officer'" class="form">
                <label>
                  联盟公告
                  <input v-model="allianceNoticeDraft" maxlength="200" placeholder="最多 200 字" />
                </label>
                <button type="button" :disabled="busy" @click="submitAllianceNotice">保存公告</button>
              </div>
              <div v-if="alliance.myRole === 'leader' || alliance.myRole === 'officer'" class="form inline">
                <label>
                  邀请角色名
                  <input v-model="inviteName" maxlength="12" />
                </label>
                <button type="button" :disabled="busy" @click="submitInvite">邀请</button>
              </div>
              <ul v-if="alliancePending?.applications.length" class="buildings">
                <li v-for="item in alliancePending.applications" :key="item.id">
                  <div>
                    <strong>{{ item.characterName }}</strong>
                    <span class="hint">申请加入</span>
                  </div>
                  <div class="actions">
                    <button type="button" :disabled="busy" @click="submitAcceptApplication(item.id)">通过</button>
                    <button type="button" :disabled="busy" @click="submitRejectApplication(item.id)">拒绝</button>
                  </div>
                </li>
              </ul>
              <p>
                <button type="button" :disabled="busy" @click="submitLeaveAlliance">退出</button>
                <button
                  v-if="alliance.myRole === 'leader'"
                  type="button"
                  :disabled="busy"
                  @click="submitDissolveAlliance"
                >
                  解散
                </button>
              </p>
            </template>
            <template v-else>
              <form class="form" @submit.prevent="submitCreateAlliance">
                <label>
                  联盟名
                  <input v-model="allianceName" maxlength="12" placeholder="2～12 位" />
                </label>
                <button type="submit" :disabled="busy">创建联盟</button>
              </form>
              <ul v-if="alliancePending?.invites.length" class="buildings">
                <li v-for="item in alliancePending.invites" :key="item.id">
                  <div>
                    <strong>{{ item.allianceName }}</strong>
                    <span class="hint">{{ item.inviterName }} 邀请</span>
                  </div>
                  <div class="actions">
                    <button type="button" :disabled="busy" @click="submitAcceptInvite(item.id)">接受</button>
                    <button type="button" :disabled="busy" @click="submitDeclineInvite(item.id)">拒绝</button>
                  </div>
                </li>
              </ul>
              <ul class="buildings">
                <li v-for="item in allianceList?.items ?? []" :key="item.id">
                  <div>
                    <strong>{{ item.name }}</strong>
                    <span class="meta">{{ item.memberCount }} 人 · 盟主 {{ item.leaderName }}</span>
                  </div>
                  <button type="button" :disabled="busy" @click="submitApplyAlliance(item.id)">申请</button>
                </li>
              </ul>
            </template>
          </section>
        </template>
      </div>
    </section>
  </main>
</template>
