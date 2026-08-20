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
  upgradeWall
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
  WorldDto
} from "./api/types";
import { clearTokens, getAccessToken, getRefreshToken, saveTokens, setUnauthorizedHandler } from "./session";
import type { HubConnection } from "@microsoft/signalr";
import WorldMap from "./WorldMap.vue";

const loading = ref(true);
const busy = ref(false);
const error = ref("");
const notice = ref("");
const mode = ref<"login" | "register">("login");
const tab = ref<"city" | "army" | "map" | "reports">("city");
const session = ref<SessionResponse | null>(null);
const overview = ref<BuildingsOverviewDto | null>(null);
const fields = ref<FieldsOverviewDto | null>(null);
const walls = ref<WallsOverviewDto | null>(null);
const army = ref<ArmyOverviewDto | null>(null);
const world = ref<WorldDto | null>(null);
const reports = ref<PagedResult<BattleReportDto> | null>(null);
const reportsPage = ref(1);
const selected = ref<MarchTarget | null>(null);
const nowMs = ref(Date.now());
const recruitType = ref("infantry");
const recruitCount = ref(10);
const marchInf = ref(20);
const marchArc = ref(0);
const marchCav = ref(0);

const username = ref("");
const password = ref("");
const characterName = ref("");

const loggedIn = computed(() => session.value !== null);
const hasCharacter = computed(() => Boolean(session.value?.character));
const hasCity = computed(() => Boolean(session.value?.city));
const queue = computed(() => overview.value?.queue ?? fields.value?.queue ?? walls.value?.queue);

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

let hub: HubConnection | null = null;
let tick: number | undefined;

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
      return "主殿等级不足";
    case "resources":
      return "资源不足";
    default:
      return "";
  }
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

async function loadAll(): Promise<void> {
  await Promise.all([loadCity(), loadArmy(), loadWorld(), loadReports()]);
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
    void disconnectHub();
  });

  tick = window.setInterval(() => {
    nowMs.value = Date.now();
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
  selected.value = target;
  tab.value = "army";
  notice.value = `已选择 ${target.label}`;
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
    password.value = "";
  }
}
</script>

<template>
  <main class="page" :class="{ wide: hasCity }">
    <header class="header">
      <h1>战国</h1>
      <p class="sub">建城 · 内政 · 城防 · 出征 · 地图</p>
    </header>

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

        <section v-else-if="!hasCity" class="block">
          <h2>{{ session?.character?.name }}</h2>
          <p class="hint">坐标由服务端在地图空地随机选取，客户端不传位置。</p>
          <button type="button" :disabled="busy" @click="submitFoundCity">建城</button>
        </section>

        <template v-else>
          <section class="block city">
            <h2>{{ session?.city?.name }}</h2>
            <p class="coord">坐标 ({{ session?.city?.x }}, {{ session?.city?.y }})</p>
            <p v-if="protectionText(army?.protectionUntil)" class="hint">{{ protectionText(army?.protectionUntil) }}</p>
          </section>

          <div class="tabs four">
            <button type="button" :class="{ active: tab === 'city' }" @click="tab = 'city'">城池</button>
            <button type="button" :class="{ active: tab === 'army' }" @click="tab = 'army'">军队</button>
            <button type="button" :class="{ active: tab === 'map' }" @click="tab = 'map'">地图</button>
            <button type="button" :class="{ active: tab === 'reports' }" @click="tab = 'reports'">战报</button>
          </div>

          <section v-if="tab === 'city' && overview" class="block">
            <h2>城内</h2>
            <p class="res">
              粮 {{ overview.resources.grain }} / 木 {{ overview.resources.wood }} / 铁 {{ overview.resources.iron }} / 铜
              {{ overview.resources.copper }}
              （上限 {{ overview.resourceCap }}，人口上限 {{ overview.populationCap }}）
            </p>
            <p v-if="queue" class="hint">
              建造中：{{ queueName(queue.buildingType) }} → {{ queue.targetLevel }} 级，剩余
              {{ remainText(queue.finishAt) }}
            </p>
            <ul class="buildings">
              <li v-for="item in overview.buildings" :key="item.type">
                <div>
                  <strong>{{ item.name }}</strong>
                  <span class="meta">{{ item.level }} / {{ item.maxLevel }} 级</span>
                  <span v-if="item.status === 'upgrading'" class="hint">
                    升级中 {{ remainText(item.finishAt) }}
                  </span>
                  <span v-else-if="blockedText(item.blockedReason)" class="hint">{{
                    blockedText(item.blockedReason)
                  }}</span>
                </div>
                <button
                  type="button"
                  :disabled="busy || item.status === 'upgrading' || Boolean(item.blockedReason)"
                  @click="submitUpgrade(item.type)"
                >
                  {{ item.level === 0 ? "建造" : "升级" }}
                </button>
              </li>
            </ul>
          </section>

          <section v-if="tab === 'city' && fields" class="block">
            <h2>城外</h2>
            <p class="res">
              可收取：良田 {{ fields.fields.find((f) => f.type === "farm")?.pending ?? 0 }} 粮 / 木场
              {{ fields.fields.find((f) => f.type === "lumber")?.pending ?? 0 }} 木 / 铁矿
              {{ fields.fields.find((f) => f.type === "ironMine")?.pending ?? 0 }} 铁 / 铜矿
              {{ fields.fields.find((f) => f.type === "copperMine")?.pending ?? 0 }} 铜
            </p>
            <p class="hint">主殿生效 1 级后可建；产出按上次收取时间现算，点收取才入库。</p>
            <p>
              <button type="button" :disabled="busy" @click="submitCollect()">一键收取</button>
            </p>
            <ul class="buildings">
              <li v-for="item in fields.fields" :key="item.type">
                <div>
                  <strong>{{ item.name }}</strong>
                  <span class="meta">{{ item.level }} / {{ item.maxLevel }} 级</span>
                  <span class="meta">{{ item.pending }} / {{ item.fieldCap }} {{ resourceLabel[item.resource] }}</span>
                  <span v-if="item.level >= 1" class="hint"> {{ item.ratePerHour }}/时 </span>
                  <span v-if="item.status === 'upgrading'" class="hint">
                    升级中 {{ remainText(item.finishAt) }}
                  </span>
                  <span v-else-if="blockedText(item.blockedReason)" class="hint">{{
                    blockedText(item.blockedReason)
                  }}</span>
                </div>
                <div class="actions">
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
            <ul class="buildings">
              <li v-for="item in walls.walls" :key="item.type">
                <div>
                  <strong>{{ item.name }}</strong>
                  <span class="meta">{{ item.level }} / {{ item.maxLevel }} 级</span>
                  <span v-if="item.status === 'upgrading'" class="hint">
                    升级中 {{ remainText(item.finishAt) }}
                  </span>
                  <span v-else-if="blockedText(item.blockedReason)" class="hint">{{
                    blockedText(item.blockedReason)
                  }}</span>
                </div>
                <button
                  type="button"
                  :disabled="busy || item.status === 'upgrading' || Boolean(item.blockedReason)"
                  @click="submitWallUpgrade(item.type)"
                >
                  {{ item.level === 0 ? "建造" : "升级" }}
                </button>
              </li>
            </ul>
          </section>

          <section v-if="tab === 'army' && army" class="block">
            <h2>军队</h2>
            <p class="res">
              步 {{ army.troops.infantry }} / 弓 {{ army.troops.archer }} / 骑 {{ army.troops.cavalry }} （上限
              {{ army.troopCap }}，兵营 {{ army.barracksLevel }} 级，城防 {{ army.wallDefense }}）
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
            <p class="hint">步兵需兵营 1 级，弓兵 2 级，骑兵 3 级。征兵即时扣资源。</p>
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
            <p class="hint">拖拽移动，滚轮缩放。金点自己，红点 AI，绿点玩家，方块为 NPC 据点。点击可选作出征目标。</p>
            <WorldMap :world="world" @select="onSelectTarget" />
          </section>

          <section v-if="tab === 'reports'" class="block">
            <h2>战报</h2>
            <p v-if="!reports?.items.length" class="hint">暂无战报</p>
            <ul class="buildings">
              <li v-for="item in reports?.items ?? []" :key="item.id">
                <div>
                  <strong>{{ item.attackerWon ? "胜" : "负" }}</strong>
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
        </template>
      </div>
    </section>
  </main>
</template>
