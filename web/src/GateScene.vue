<script setup lang="ts">
import { computed } from "vue";
import { dayPhase, dayPhaseLabel } from "./format";

const props = defineProps<{
  nowMs: number;
  stage: "auth" | "character" | "found";
}>();

const phase = computed(() => dayPhase(props.nowMs));
const stageLine = computed(() => {
  switch (props.stage) {
    case "character":
      return "立旗为号，先定名号再入城。";
    case "found":
      return "择空地立城，城门一开便是你的封疆。";
    default:
      return "过城门入局。登录或注册后即可创角建城。";
  }
});
</script>

<template>
  <div class="scene-shell">
    <div class="scene gate" :class="phase">
      <div class="sky"></div>
      <div class="ridge"></div>
      <div class="haze"></div>
      <div class="ground"></div>
      <div class="plaza"></div>
      <div class="wall-band left"></div>
      <div class="wall-band right"></div>
      <div class="wall-band top"></div>
      <div class="gate-road"></div>
      <i class="bird a"></i>
      <i class="bird b"></i>
      <i class="walker a"></i>
      <i class="walker b"></i>
      <i class="banner left"></i>
      <i class="banner right"></i>
      <i v-for="n in 8" :key="n" class="firefly" :style="{ '--i': n }"></i>
      <i class="torch a"></i>
      <i class="torch b"></i>
      <div class="plot landmark palace-mark">
        <span class="shadow"></span>
        <img src="/art/palace.jpg" alt="王城" />
        <b class="name">王城</b>
      </div>
      <div class="plot landmark gate-mark">
        <span class="shadow"></span>
        <img src="/art/gate.jpg" alt="城门" />
        <b class="name">城门</b>
      </div>
      <div class="scene-title">
        <h1>战国</h1>
        <p>{{ dayPhaseLabel(phase) }} · {{ stageLine }}</p>
      </div>
    </div>
    <aside class="plot-dock form-dock">
      <slot />
    </aside>
  </div>
</template>
