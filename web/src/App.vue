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
  aidMarket,
  scout,
  fetchDaily,
  claimDaily,
  fetchShop,
  buyShopItem,
  useShopItem
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
  MarketsOverviewDto,
  MarchDto,
  TroopDto,
  DailyOverviewDto,
  ShopOverviewDto,
  ShopCatalogItemDto,
  BuildingQueueDto
} from "./api/types";
import { clearTokens, getAccessToken, getRefreshToken, saveTokens, setUnauthorizedHandler } from "./session";
import type { HubConnection } from "@microsoft/signalr";
import WorldMap from "./WorldMap.vue";
import CityScene from "./CityScene.vue";
import OuterScene from "./OuterScene.vue";
import GateScene from "./GateScene.vue";
import ShopScene from "./ShopScene.vue";
import BarracksScene from "./BarracksScene.vue";
import { resourceArt, resourceKeys } from "./art";
import { gameAudio } from "./audio";
import { calibratedNow, incomingOnCity, remainText as formatRemain, resourceLabel, slotLine, troopLabel } from "./format";

const loading = ref(true);
const busy = ref(false);
const error = ref("");
const notice = ref("");
const mode = ref<"login" | "register">("login");
const tab = ref<"city" | "army" | "daily" | "map" | "reports" | "mail" | "ranks" | "alliance" | "market" | "shop">(
  "city"
);
const cityZone = ref<"inner" | "outer" | "wall">("inner");
const muted = ref(gameAudio.muted);
const flyChips = ref<{ id: number; key: string; amount: number }[]>([]);
let flySeq = 0;
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
const clock = ref({ clientAt: Date.now(), serverAt: Date.now() });
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
const daily = ref<DailyOverviewDto | null>(null);
const shop = ref<ShopOverviewDto | null>(null);
const relocateX = ref(0);
const relocateY = ref(0);

const username = ref("");
const password = ref("");
const characterName = ref("");

const loggedIn = computed(() => session.value !== null);
const hasCharacter = computed(() => Boolean(session.value?.character));
const hasCity = computed(() => Boolean(session.value?.city));
const buildQueues = computed(() => {
  const map = new Map<string, BuildingQueueDto>();
  for (const item of [
    ...(overview.value?.queues ?? (overview.value?.queue ? [overview.value.queue] : [])),
    ...(fields.value?.queues ?? (fields.value?.queue ? [fields.value.queue] : [])),
    ...(walls.value?.queues ?? (walls.value?.queue ? [walls.value.queue] : []))
  ]) {
    map.set(item.buildingType, item);
  }
  return [...map.values()];
});
const recruitQueues = computed(() =>
  army.value?.recruitQueues?.length
    ? army.value.recruitQueues
    : army.value?.recruitQueue
      ? [army.value.recruitQueue]
      : []
);
const hudResources = computed(
  () =>
    overview.value?.resources ??
    fields.value?.resources ??
    walls.value?.resources ??
    army.value?.resources ??
    daily.value?.resources ??
    markets.value?.resources ??
    null
);
const hudCap = computed(
  () =>
    overview.value?.resourceCap ??
    fields.value?.resourceCap ??
    walls.value?.resourceCap ??
    army.value?.resourceCap ??
    daily.value?.resourceCap ??
    markets.value?.resourceCap ??
    0
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

const selectedProtected = computed(() => {
  if (selected.value?.targetType !== "city") {
    return false;
  }
  return Boolean(world.value?.cities.find((item) => item.id === selected.value?.targetId)?.protected);
});

const selectedSelf = computed(() => {
  if (selected.value?.targetType !== "city") {
    return false;
  }
  return selected.value.targetId === session.value?.city?.id;
});

const dailyClaimable = computed(
  () => daily.value?.missions.filter((item) => !item.claimed && item.progress >= item.required).length ?? 0
);

const marchInvalid = computed(() => {
  if (!army.value) {
    return "没有军队";
  }
  const infantry = Number.isFinite(marchInf.value) ? Math.max(0, marchInf.value) : 0;
  const archer = Number.isFinite(marchArc.value) ? Math.max(0, marchArc.value) : 0;
  const cavalry = Number.isFinite(marchCav.value) ? Math.max(0, marchCav.value) : 0;
  if (infantry + archer + cavalry <= 0) {
    return "出征至少需要 1 名士兵";
  }
  if (
    infantry > army.value.troops.infantry ||
    archer > army.value.troops.archer ||
    cavalry > army.value.troops.cavalry
  ) {
    return "派出兵力超过驻军";
  }
  return "";
});

const scoutInvalid = computed(() => {
  if (!army.value) {
    return "没有军队";
  }
  if (army.value.barracksLevel < 1) {
    return "需要兵营 1 级";
  }
  if (army.value.troops.infantry < 1) {
    return "斥候需要 1 名步兵";
  }
  if (selectedSelf.value) {
    return "不能侦察自己的城";
  }
  return "";
});

function troopLine(t?: TroopDto): string {
  if (!t) {
    return "";
  }
  return `步${t.infantry} 弓${t.archer} 骑${t.cavalry}`;
}

function marchTargetName(item: MarchDto): string {
  if (item.targetType === "outpost") {
    return world.value?.outposts.find((outpost) => outpost.id === item.targetId)?.name ?? "据点";
  }
  return world.value?.cities.find((city) => city.id === item.targetId)?.name ?? "城池";
}

function marchKindLabel(item: MarchDto): string {
  return item.kind === "scout" ? "斥候" : "出征";
}

function missionWidth(progress: number, required: number): string {
  if (required <= 0) {
    return "0%";
  }
  return `${Math.max(0, Math.min(100, (progress / required) * 100))}%`;
}

function rewardParts(reward: { grain: number; wood: number; iron: number; copper: number }) {
  return resourceKeys
    .map((key) => ({ key, amount: reward[key] }))
    .filter((item) => item.amount > 0);
}

const syncedNow = computed(() => clock.value.serverAt + (nowMs.value - clock.value.clientAt));

const incomingMarches = computed(() => {
  const city = session.value?.city;
  if (!city || !world.value) {
    return [];
  }
  return incomingOnCity(world.value.marches, city.id);
});

const allianceRoleLabel: Record<string, string> = {
  leader: "盟主",
  officer: "官员",
  member: "成员"
};

const mailTypeLabel: Record<string, string> = {
  system: "系统",
  battle: "战报",
  alliance: "联盟",
  scout: "斥候"
};

let hub: HubConnection | null = null;
let tick: number | undefined;
let lastWorldRefresh = 0;

function unlockAudio(): void {
  gameAudio.unlock();
}

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
  return formatRemain(finishAt, syncedNow.value);
}

function markClock(serverTime?: string): void {
  const clientAt = Date.now();
  clock.value = {
    clientAt,
    serverAt: calibratedNow(serverTime, clientAt, clientAt)
  };
}

function setCityZone(zone: "inner" | "outer" | "wall"): void {
  cityZone.value = zone;
  gameAudio.play("click");
  syncAmbient();
}

function toggleMute(): void {
  muted.value = gameAudio.toggleMuted();
  if (!muted.value) {
    gameAudio.unlock();
    syncAmbient();
  }
}

function syncAmbient(): void {
  if (!loggedIn.value || !hasCity.value) {
    gameAudio.setAmbient("login");
    return;
  }
  if (tab.value === "map") {
    gameAudio.setAmbient("map");
    return;
  }
  if (tab.value === "shop") {
    gameAudio.setAmbient("shop");
    return;
  }
  if (tab.value === "army") {
    gameAudio.setAmbient("army");
    return;
  }
  if (tab.value === "city" && cityZone.value === "outer") {
    gameAudio.setAmbient("outer");
    return;
  }
  if (tab.value === "city" && cityZone.value === "wall") {
    gameAudio.setAmbient("wall");
    return;
  }
  gameAudio.setAmbient("city");
}

function spawnCollectFx(collected: { grain: number; wood: number; iron: number; copper: number }): void {
  const chips = resourceKeys
    .filter((key) => collected[key] > 0)
    .map((key) => ({ id: ++flySeq, key, amount: collected[key] }));
  flyChips.value = chips;
  window.setTimeout(() => {
    if (flyChips.value === chips) {
      flyChips.value = [];
    }
  }, 1100);
}

function protectionText(until?: string): string {
  if (!until) {
    return "";
  }
  const ms = Date.parse(until) - syncedNow.value;
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
  markClock(outer.serverTime ?? inner.serverTime ?? wall.serverTime);
}

async function loadArmy(): Promise<void> {
  army.value = await fetchArmy();
}

async function loadWorld(): Promise<void> {
  world.value = await fetchWorld();
  markClock(world.value.serverTime);
}

async function loadMarkets(): Promise<void> {
  markets.value = await fetchMarkets();
  if (selectedMarketId.value == null && markets.value.markets.length > 0) {
    selectedMarketId.value = markets.value.markets[0].id;
  }
}

async function loadDaily(): Promise<void> {
  daily.value = await fetchDaily();
}

async function loadShop(): Promise<void> {
  const data = await fetchShop();
  if (!shop.value) {
    relocateX.value = data.x;
    relocateY.value = data.y;
  }
  shop.value = data;
  if (session.value?.city) {
    session.value = {
      ...session.value,
      city: {
        ...session.value.city,
        x: data.x,
        y: data.y
      }
    };
  }
  if (data.protectionUntil && army.value) {
    army.value = { ...army.value, protectionUntil: data.protectionUntil };
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
  const [list, pending, mine] = await Promise.all([
    fetchAlliances(),
    fetchAlliancePending(),
    fetchMyAlliance()
  ]);
  allianceList.value = list;
  alliancePending.value = pending;
  alliance.value = mine;
  allianceNoticeDraft.value = mine?.notice ?? "";
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
    loadMarkets(),
    loadDaily(),
    loadShop()
  ]);
}

async function connectHub(): Promise<void> {
  await disconnectHub();
  hub = createGameHub();
  hub.on("BuildComplete", () => {
    void loadCity();
    error.value = "";
    notice.value = "建造完成";
    gameAudio.play("complete");
  });
  hub.on("MarchArrived", (payload?: { data?: unknown }) => {
    void loadAll();
    error.value = "";
    notice.value = payload?.data ? "部队已到达" : "斥候已回报";
    gameAudio.play(payload?.data ? "attack" : "scout");
  });
  hub.on("CityAttacked", () => {
    void loadAll();
    error.value = "";
    notice.value = "本城遭到攻击";
    gameAudio.play("attack");
  });
  hub.on("TransportArrived", () => {
    void loadAll();
    error.value = "";
    notice.value = "运输已到达";
    gameAudio.play("transport");
  });
  hub.on("ResourceReceived", () => {
    void loadAll();
    error.value = "";
    notice.value = "收到同盟资源";
    gameAudio.play("collect");
  });
  hub.on("RecruitComplete", () => {
    void loadArmy();
    error.value = "";
    notice.value = "征兵完成";
    gameAudio.play("recruit");
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
    hub.off("RecruitComplete");
    await hub.stop();
    hub = null;
  }
}

onMounted(async () => {
  setUnauthorizedHandler(() => {
    session.value = null;
    gameAudio.setAmbient("login");
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
    daily.value = null;
    shop.value = null;
    void disconnectHub();
  });

  window.addEventListener("pointerdown", unlockAudio, { once: true });
  syncAmbient();

  tick = window.setInterval(() => {
    nowMs.value = Date.now();
    const gap = tab.value === "map" ? 5000 : 12000;
    if (hasCity.value && Date.now() - lastWorldRefresh >= gap) {
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
  window.removeEventListener("pointerdown", unlockAudio);
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
    daily.value = null;
    shop.value = null;
    await disconnectHub();
    return;
  }
  try {
    await loadAll();
    await connectHub();
    syncAmbient();
  } catch (err) {
    fail(err);
  }
});

watch(tab, (value) => {
  syncAmbient();
  if (value === "map" && hasCity.value) {
    lastWorldRefresh = Date.now();
    void loadWorld().catch(fail);
  }
  if (value === "daily" && hasCity.value) {
    void loadDaily().catch(fail);
  }
  if (value === "shop" && hasCity.value) {
    void loadShop().catch(fail);
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
    gameAudio.unlock();
    syncAmbient();
  } catch (err) {
    fail(err);
    gameAudio.play("error");
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
    await Promise.all([loadCity(), loadDaily()]);
    notice.value = `已开始${queueName(type) || "建造"}`;
    gameAudio.play("build");
  } catch (err) {
    fail(err);
    gameAudio.play("error");
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
    await Promise.all([loadCity(), loadDaily()]);
    notice.value = `已开始${queueName(type) || "建造"}`;
    gameAudio.play("build");
  } catch (err) {
    fail(err);
    gameAudio.play("error");
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
    await Promise.all([loadCity(), loadDaily()]);
    notice.value = `已开始${queueName(type) || "建造"}`;
    gameAudio.play("build");
  } catch (err) {
    fail(err);
    gameAudio.play("error");
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
      queues: fields.value?.queues,
      fieldSlots: fields.value?.fieldSlots,
      fields: result.data.fields
    };
    markClock(result.data.serverTime);
    if (overview.value) {
      overview.value = { ...overview.value, resources: result.data.resources, resourceCap: result.data.resourceCap };
    }
    if (walls.value) {
      walls.value = { ...walls.value, resources: result.data.resources, resourceCap: result.data.resourceCap };
    }
    const collected = result.data.collected;
    const parts = resourceKeys
      .filter((key) => collected[key] > 0)
      .map((key) => `${resourceLabel[key]}${collected[key]}`);
    if (parts.length) {
      notice.value = `已收取 ${parts.join(" ")}`;
      spawnCollectFx(collected);
      gameAudio.play("collect");
      await loadDaily();
    } else if (result.message && result.message !== "ok") {
      notice.value = result.message;
    } else {
      notice.value = "没有可收取的资源";
    }
  } catch (err) {
    fail(err);
    gameAudio.play("error");
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
    await loadDaily();
    notice.value = recruitQueues.value.length
      ? `已下达征兵 ${recruitCount.value} 名${troopLabel[recruitType.value] ?? "士兵"}，队列 ${army.value.recruitSlots?.used ?? recruitQueues.value.length}/${army.value.recruitSlots?.limit ?? 5}`
      : `已征${recruitCount.value}名${troopLabel[recruitType.value] ?? "士兵"}`;
    gameAudio.play("recruit");
  } catch (err) {
    fail(err);
    gameAudio.play("error");
  } finally {
    busy.value = false;
  }
}

async function submitMarch(): Promise<void> {
  if (!selected.value) {
    error.value = "请先在地图上点选目标";
    return;
  }
  if (selectedSelf.value) {
    error.value = "不能进攻自己的城";
    return;
  }
  if (selectedProtected.value) {
    error.value = "该城处于保护期，无法进攻";
    return;
  }
  if (marchInvalid.value) {
    error.value = marchInvalid.value;
    return;
  }
  const infantry = Number.isFinite(marchInf.value) ? Math.max(0, marchInf.value) : 0;
  const archer = Number.isFinite(marchArc.value) ? Math.max(0, marchArc.value) : 0;
  const cavalry = Number.isFinite(marchCav.value) ? Math.max(0, marchCav.value) : 0;
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
    await Promise.all([loadWorld(), loadReports(), loadCity(), loadDaily()]);
    tab.value = "map";
    notice.value = "已出征，队伍正在赶路";
    gameAudio.play("march");
  } catch (err) {
    fail(err);
    gameAudio.play("error");
  } finally {
    busy.value = false;
  }
}

async function submitScout(): Promise<void> {
  if (!selected.value) {
    error.value = "请先在地图上点选目标";
    return;
  }
  if (scoutInvalid.value) {
    error.value = scoutInvalid.value;
    return;
  }
  error.value = "";
  notice.value = "";
  busy.value = true;
  try {
    army.value = await scout(selected.value.targetType, selected.value.targetId);
    await Promise.all([loadWorld(), loadMail(), loadDaily()]);
    tab.value = "map";
    notice.value = "斥候已出发，到点后看邮件";
    gameAudio.play("scout");
  } catch (err) {
    fail(err);
    gameAudio.play("error");
  } finally {
    busy.value = false;
  }
}

async function submitClaimDaily(missionType: string): Promise<void> {
  await run(async () => {
    daily.value = await claimDaily(missionType);
    await loadCity();
    notice.value = "已领取犒赏";
    gameAudio.play("claim");
  });
}

function onSelectTarget(target: MarchTarget): void {
  error.value = "";
  if (target.targetType === "market") {
    selected.value = null;
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
    await Promise.all([loadCity(), loadWorld(), loadMail(), loadDaily()]);
    notice.value = "已出发前往市集";
    gameAudio.play("transport");
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
    gameAudio.play("transport");
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
    gameAudio.play("error");
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

async function submitBuy(item: ShopCatalogItemDto, count = 1): Promise<void> {
  await run(async () => {
    const qty = Math.max(1, Math.min(99, Math.floor(count) || 1));
    shop.value = await buyShopItem(item.type, qty);
    notice.value = `已购买 ${item.name} × ${qty}`;
    gameAudio.play("claim");
  });
}

async function submitUse(item: ShopCatalogItemDto, count = 1): Promise<void> {
  await run(async () => {
    const qty = item.kind === "buff" ? Math.max(1, Math.min(99, Math.floor(count) || 1)) : 1;
    if (item.type === "relocateTarget") {
      shop.value = await useShopItem(item.type, 1, relocateX.value, relocateY.value);
    } else {
      shop.value = await useShopItem(item.type, qty);
    }
    if (session.value?.city && shop.value) {
      session.value = {
        ...session.value,
        city: { ...session.value.city, x: shop.value.x, y: shop.value.y }
      };
    }
    await Promise.all([loadCity(), loadArmy(), loadWorld(), loadShop()]);
    notice.value = item.type.startsWith("relocate") ? "迁城成功" : `已使用 ${item.name}`;
    gameAudio.play(item.type.startsWith("relocate") ? "march" : "claim");
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
    gameAudio.setAmbient("login");
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
    markets.value = null;
    daily.value = null;
    shop.value = null;
  }
}
</script>

<template>
  <div class="fly-layer">
    <span v-for="chip in flyChips" :key="chip.id" class="fly-chip">
      <img :src="resourceArt[chip.key]" :alt="resourceLabel[chip.key]" />
      +{{ chip.amount }}
    </span>
  </div>
  <main class="page" :class="{ wide: hasCity, 'scene-page': !hasCity }">
    <div v-if="hasCity" class="hud-stack">
      <header class="hud-top">
        <div class="brand">
          <img class="brand-seal" src="/art/palace.jpg" alt="" />
          <div>
            <h1>战国</h1>
            <p class="city-name">
              {{ session?.city?.name }} · ({{ session?.city?.x }}, {{ session?.city?.y }})
              <span v-if="protectionText(army?.protectionUntil ?? shop?.protectionUntil)" class="protect">
                {{ protectionText(army?.protectionUntil ?? shop?.protectionUntil) }}
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
          <span class="res-chip yuanbao">
            <b>{{ shop?.yuanbao ?? 0 }}</b>
            <small>元宝</small>
          </span>
        </div>
        <div class="who">
          <span>{{ session?.username }}</span>
          <button type="button" class="link sound-toggle" @click="toggleMute">{{ muted ? "音效关" : "音效开" }}</button>
          <button type="button" class="link" @click="submitLogout">退出</button>
        </div>
      </header>
      <p v-if="hudResources" class="hint cap-line">仓库上限 {{ hudCap }}<template v-if="overview"> · 人口上限 {{ overview.populationCap }}</template></p>
      <div class="tabs play">
        <button type="button" :class="{ active: tab === 'city' }" @click="tab = 'city'">城池</button>
        <button type="button" :class="{ active: tab === 'army' }" @click="tab = 'army'">军队</button>
        <button type="button" :class="{ active: tab === 'daily' }" @click="tab = 'daily'">
          军务{{ dailyClaimable ? ` ${dailyClaimable}` : "" }}
        </button>
        <button type="button" :class="{ active: tab === 'map' }" @click="tab = 'map'">地图</button>
        <button type="button" :class="{ active: tab === 'market' }" @click="tab = 'market'">市集</button>
        <button type="button" :class="{ active: tab === 'shop' }" @click="tab = 'shop'">商城</button>
        <button type="button" :class="{ active: tab === 'reports' }" @click="tab = 'reports'">战报</button>
        <button type="button" :class="{ active: tab === 'mail' }" @click="tab = 'mail'">
          邮件{{ mail?.unreadCount ? ` ${mail.unreadCount}` : "" }}
        </button>
        <button type="button" :class="{ active: tab === 'ranks' }" @click="tab = 'ranks'">排行</button>
        <button type="button" :class="{ active: tab === 'alliance' }" @click="tab = 'alliance'">联盟</button>
      </div>
      <p v-if="buildQueues.length" class="queue-banner">
        建造中 {{ buildQueues.length }} 项：
        <span v-for="item in buildQueues" :key="item.buildingType">
          {{ queueName(item.buildingType) }} → {{ item.targetLevel }} 级 {{ remainText(item.finishAt) }}
        </span>
      </p>
      <p v-if="recruitQueues.length" class="queue-banner">
        征兵中 {{ recruitQueues.length }} 项：
        <span v-for="(item, index) in recruitQueues" :key="`${item.troopType}-${index}`">
          {{ troopLabel[item.troopType] ?? item.troopType }} × {{ item.count }} {{ remainText(item.finishAt) }}
        </span>
      </p>
      <p v-if="incomingMarches.length" class="queue-banner incoming-banner">
        敌军逼近本城 {{ incomingMarches.length }} 路
      </p>
    </div>

    <p v-if="loading" class="hint">加载中…</p>

    <GateScene
      v-else-if="!hasCity"
      :now-ms="nowMs"
      :stage="!loggedIn ? 'auth' : !hasCharacter ? 'character' : 'found'"
      @click="gameAudio.unlock()"
    >
      <p v-if="error" class="error toast">{{ error }}</p>
      <p v-if="notice" class="hint toast">{{ notice }}</p>
      <p>
        <button type="button" class="link" @click="toggleMute">{{ muted ? "音效关" : "音效开" }}</button>
      </p>
      <form v-if="!loggedIn" class="form" @submit.prevent="submitAuth">
        <div class="tabs">
          <button type="button" :class="{ active: mode === 'login' }" @click="mode = 'login'; gameAudio.play('click')">
            登录
          </button>
          <button
            type="button"
            :class="{ active: mode === 'register' }"
            @click="mode = 'register'; gameAudio.play('click')"
          >
            注册
          </button>
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
        <button type="submit" :disabled="busy">{{ mode === "register" ? "注册并入城" : "登录入城" }}</button>
      </form>
      <div v-else>
        <div class="who">
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
        <section v-else class="block">
          <h2>{{ session?.character?.name }}</h2>
          <p class="hint">坐标由服务端在地图空地随机选取，客户端不传位置。</p>
          <button type="button" :disabled="busy" @click="submitFoundCity">立城开局</button>
        </section>
      </div>
    </GateScene>

    <section v-else class="card">
      <p v-if="error" class="error toast">{{ error }}</p>
      <p v-if="notice" class="hint toast">{{ notice }}</p>

      <div class="play">
          <section v-if="tab === 'city'" class="block city-play">
            <div class="zone-tabs">
              <button type="button" :class="{ active: cityZone === 'inner' }" @click="setCityZone('inner')">城内</button>
              <button type="button" :class="{ active: cityZone === 'outer' }" @click="setCityZone('outer')">城外</button>
              <button type="button" :class="{ active: cityZone === 'wall' }" @click="setCityZone('wall')">城墙</button>
            </div>
            <CityScene
              v-if="cityZone === 'inner' && overview"
              mode="inner"
              :items="overview.buildings"
              :busy="busy"
              :now-ms="syncedNow"
              :slot-line="[slotLine(overview.buildSlots, '建造'), slotLine(overview.techSlots, '科技')].filter(Boolean).join(' · ')"
              @upgrade="submitUpgrade"
              @pick="gameAudio.play('click')"
            />
            <OuterScene
              v-else-if="cityZone === 'outer' && fields"
              :items="fields.fields"
              :busy="busy"
              :now-ms="syncedNow"
              :server-time="fields.serverTime"
              :slot-line="slotLine(fields.fieldSlots, '资源')"
              @upgrade="submitFieldUpgrade"
              @collect="submitCollect"
              @pick="gameAudio.play('click')"
            />
            <CityScene
              v-else-if="cityZone === 'wall' && walls"
              mode="wall"
              :items="walls.walls"
              :busy="busy"
              :now-ms="syncedNow"
              :wall-defense="walls.wallDefense"
              :trap-bonus="walls.trapBonus"
              :threatened="incomingMarches.length > 0"
              :slot-line="slotLine(walls.buildSlots, '建造')"
              @upgrade="submitWallUpgrade"
              @pick="gameAudio.play('click')"
            />
          </section>

          <section v-if="tab === 'army' && army" class="block city-play">
            <BarracksScene
              :army="army"
              :busy="busy"
              :now-ms="syncedNow"
              v-model:troop-type="recruitType"
              v-model:count="recruitCount"
              @recruit="submitRecruit"
              @pick="gameAudio.play('click')"
            />
            <div class="war-table">
              <h3>出征 / 侦察</h3>
              <p :class="selectedProtected ? 'lose' : 'hint'">
                {{ selected ? `目标：${selected.label}` : "在地图点选据点或其他玩家城" }}
              </p>
              <p v-if="selectedSelf" class="hint">不能侦察或进攻自己的城</p>
              <p v-else-if="selectedProtected" class="lose">保护中的城不能进攻，但仍可派斥候查看</p>
              <p class="hint">驻军 {{ troopLine(army.troops) }}。斥候固定派出 1 名步兵，半程到达，不战斗。</p>
              <p v-if="marchInvalid && selected" class="hint">{{ marchInvalid }}</p>
              <p v-if="scoutInvalid && selected" class="hint">{{ scoutInvalid }}</p>
              <div class="form inline">
                <label>步 <input v-model.number="marchInf" type="number" min="0" :max="army.troops.infantry" /></label>
                <label>弓 <input v-model.number="marchArc" type="number" min="0" :max="army.troops.archer" /></label>
                <label>骑 <input v-model.number="marchCav" type="number" min="0" :max="army.troops.cavalry" /></label>
                <button type="button" :disabled="busy || !selected || selectedSelf || selectedProtected || Boolean(marchInvalid)" @click="submitMarch">出征</button>
                <button type="button" :disabled="busy || !selected || Boolean(scoutInvalid)" @click="submitScout">侦察</button>
              </div>
              <ul class="buildings">
                <li v-for="item in army.marches" :key="item.id">
                  <div>
                    <strong>{{ marchKindLabel(item) }} · {{ marchTargetName(item) }}</strong>
                    <span class="meta">{{ item.fromX }},{{ item.fromY }} → {{ item.toX }},{{ item.toY }}</span>
                    <span class="hint">到达 {{ remainText(item.arriveAt) }}</span>
                    <p v-if="troopLine(item.troops)" class="hint">{{ troopLine(item.troops) }}</p>
                  </div>
                </li>
              </ul>
            </div>
          </section>

          <section v-if="tab === 'daily' && daily" class="block">
            <h2>每日军务</h2>
            <p class="hint">按 UTC 自然日刷新。进度由成功指令自动累计，昨天未领的犒赏作废。</p>
            <ul class="buildings">
              <li v-for="item in daily.missions" :key="item.type">
                <div>
                  <strong>{{ item.name }}</strong>
                  <span class="meta">{{ item.progress }}/{{ item.required }}</span>
                  <p class="hint">{{ item.detail }}</p>
                  <div class="mission-bar" aria-hidden="true">
                    <i :style="{ width: missionWidth(item.progress, item.required) }"></i>
                  </div>
                  <div class="cost-row">
                    <span v-for="part in rewardParts(item.reward)" :key="part.key" class="cost-chip">
                      <img :src="resourceArt[part.key]" :alt="resourceLabel[part.key]" />
                      {{ part.amount }}
                    </span>
                  </div>
                </div>
                <button
                  v-if="!item.claimed"
                  type="button"
                  :disabled="busy || item.progress < item.required"
                  @click="submitClaimDaily(item.type)"
                >
                  {{ item.progress < item.required ? "未完成" : "领取" }}
                </button>
                <span v-else class="meta">已领</span>
              </li>
            </ul>
          </section>

          <section v-show="tab === 'map'" v-if="world" class="block">
            <h2>大地图</h2>
            <div class="legend">
              <span><img src="/art/marker-city.jpg" alt="" />城池（金圈自己 / 红圈 AI / 绿圈玩家）</span>
              <span><img src="/art/marker-outpost.jpg" alt="" />常驻据点</span>
              <span><img src="/art/marker-roaming.jpg" alt="" />限时流寇</span>
              <span><img src="/art/marker-market.jpg" alt="" />市集</span>
              <span><img src="/art/marker-march.jpg" alt="" />行军（金线出征 / 青线斥候）</span>
              <span><img src="/art/marker-cart.jpg" alt="" />运输马车</span>
            </div>
            <p class="hint">
              地形、天气与昼夜会随时间变化；行军扬尘、流寇脉冲和标签实时更新。拖拽移动，滚轮对着指针缩放，点「回城」回到本城。
            </p>
            <WorldMap :world="world" :active="tab === 'map'" @select="onSelectTarget" />
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

          <section v-if="tab === 'shop' && shop" class="block city-play">
            <ShopScene
              :shop="shop"
              :busy="busy"
              :now-ms="syncedNow"
              v-model:relocate-x="relocateX"
              v-model:relocate-y="relocateY"
              @buy="submitBuy"
              @use="submitUse"
              @pick="gameAudio.play('click')"
            />
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
                    攻 {{ troopLine(item.attackerBefore) }} → {{ troopLine(item.attackerAfter) }} /
                    守 {{ troopLine(item.defenderBefore) }} → {{ troopLine(item.defenderAfter) }}
                    <template v-if="item.yuanbao"> · 元宝 +{{ item.yuanbao }}</template>
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
                  <span class="meta">{{ mailTypeLabel[item.type] ?? item.type }}</span>
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
      </div>
    </section>
  </main>
</template>
