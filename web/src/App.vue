<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import {
  createCharacter,
  fetchBuildings,
  fetchSession,
  foundCity,
  login,
  logout,
  register,
  upgradeBuilding
} from "./api/game";
import { createGameHub } from "./api/hub";
import { ApiError } from "./api/types";
import type { BuildingsOverviewDto, SessionResponse } from "./api/types";
import { clearTokens, getAccessToken, getRefreshToken, saveTokens } from "./session";
import type { HubConnection } from "@microsoft/signalr";

const loading = ref(true);
const busy = ref(false);
const error = ref("");
const mode = ref<"login" | "register">("login");
const session = ref<SessionResponse | null>(null);
const overview = ref<BuildingsOverviewDto | null>(null);
const nowMs = ref(Date.now());

const username = ref("");
const password = ref("");
const characterName = ref("");

const loggedIn = computed(() => session.value !== null);
const hasCharacter = computed(() => Boolean(session.value?.character));
const hasCity = computed(() => Boolean(session.value?.city));

let hub: HubConnection | null = null;
let tick: number | undefined;

function fail(err: unknown): void {
  error.value = err instanceof ApiError || err instanceof Error ? err.message : "操作失败";
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

async function loadBuildings(): Promise<void> {
  overview.value = await fetchBuildings();
}

async function connectHub(): Promise<void> {
  await disconnectHub();
  hub = createGameHub();
  hub.on("BuildComplete", () => {
    void loadBuildings();
  });
  await hub.start();
}

async function disconnectHub(): Promise<void> {
  if (hub) {
    hub.off("BuildComplete");
    await hub.stop();
    hub = null;
  }
}

onMounted(async () => {
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
  if (tick !== undefined) {
    window.clearInterval(tick);
  }
  void disconnectHub();
});

watch(hasCity, async (ready) => {
  if (!ready) {
    overview.value = null;
    await disconnectHub();
    return;
  }
  try {
    await loadBuildings();
    await connectHub();
  } catch (err) {
    fail(err);
  }
});

async function submitAuth(): Promise<void> {
  error.value = "";
  busy.value = true;
  try {
    const tokens =
      mode.value === "register"
        ? await register(username.value.trim(), password.value)
        : await login(username.value.trim(), password.value);
    saveTokens(tokens.accessToken, tokens.refreshToken);
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
  busy.value = true;
  try {
    overview.value = await upgradeBuilding(type);
  } catch (err) {
    fail(err);
  } finally {
    busy.value = false;
  }
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
    password.value = "";
  }
}
</script>

<template>
  <main class="page" :class="{ wide: hasCity }">
    <header class="header">
      <h1>战国</h1>
      <p class="sub">账号 · 角色 · 建城 · 城内</p>
    </header>

    <p v-if="loading" class="hint">加载中…</p>

    <section v-else class="card">
      <p v-if="error" class="error">{{ error }}</p>

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

        <section v-else class="block">
          <h2>角色</h2>
          <p>{{ session?.character?.name }}</p>
        </section>

        <section v-if="hasCharacter && !hasCity" class="block">
          <h2>建立主城</h2>
          <p class="hint">坐标由服务端在地图空地随机选取，客户端不传位置。</p>
          <button type="button" :disabled="busy" @click="submitFoundCity">建城</button>
        </section>

        <section v-if="hasCity" class="block city">
          <h2>{{ session?.city?.name }}</h2>
          <p class="coord">坐标 ({{ session?.city?.x }}, {{ session?.city?.y }})</p>
        </section>

        <section v-if="overview" class="block">
          <h2>城内</h2>
          <p class="res">
            粮 {{ overview.resources.grain }} / 木 {{ overview.resources.wood }} / 铁 {{ overview.resources.iron }} / 铜
            {{ overview.resources.copper }}
            （上限 {{ overview.resourceCap }}，人口上限 {{ overview.populationCap }}）
          </p>
          <p v-if="overview.queue" class="hint">
            建造中：{{ overview.queue.buildingType }} → {{ overview.queue.targetLevel }} 级，剩余
            {{ remainText(overview.queue.finishAt) }}
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
      </div>
    </section>
  </main>
</template>
