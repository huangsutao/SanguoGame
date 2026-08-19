<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import {
  createCharacter,
  fetchSession,
  foundCity,
  login,
  logout,
  register
} from "./api/game";
import { ApiError } from "./api/types";
import type { SessionResponse } from "./api/types";
import { clearTokens, getAccessToken, getRefreshToken, saveTokens } from "./session";

const loading = ref(true);
const busy = ref(false);
const error = ref("");
const mode = ref<"login" | "register">("login");
const session = ref<SessionResponse | null>(null);

const username = ref("");
const password = ref("");
const characterName = ref("");

const loggedIn = computed(() => session.value !== null);
const hasCharacter = computed(() => Boolean(session.value?.character));
const hasCity = computed(() => Boolean(session.value?.city));

onMounted(async () => {
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

function fail(err: unknown): void {
  error.value = err instanceof ApiError || err instanceof Error ? err.message : "操作失败";
}

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
    clearTokens();
    session.value = null;
    password.value = "";
  }
}
</script>

<template>
  <main class="page">
    <header class="header">
      <h1>战国</h1>
      <p class="sub">第 1 步：账号 · 角色 · 建城</p>
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
          <p class="hint">城内 / 城墙 / 城外建筑将在后续步骤开放。</p>
        </section>
      </div>
    </section>
  </main>
</template>
