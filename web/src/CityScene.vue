<script setup lang="ts">
import { computed, ref } from "vue";
import type { BuildingItemDto } from "./api/types";
import { buildingPortrait, resourceArt } from "./art";
import { blockedText, costParts, dayPhase, effectsText, remainText, resourceLabel } from "./format";

const props = defineProps<{
  mode: "inner" | "wall";
  items: BuildingItemDto[];
  busy?: boolean;
  nowMs: number;
  wallDefense?: number;
  trapBonus?: number;
  threatened?: boolean;
}>();

const emit = defineEmits<{
  upgrade: [type: string];
  pick: [];
}>();

const layouts: Record<
  "inner" | "wall",
  Record<string, { x: number; y: number; w: number; z: number }>
> = {
  inner: {
    palace: { x: 50, y: 27, w: 16, z: 2 },
    house: { x: 27, y: 40, w: 12, z: 3 },
    warehouse: { x: 73, y: 40, w: 12, z: 3 },
    academy: { x: 18, y: 58, w: 11, z: 4 },
    drillHall: { x: 38, y: 56, w: 11, z: 4 },
    defenseHall: { x: 62, y: 56, w: 11, z: 4 },
    resourceHall: { x: 82, y: 58, w: 11, z: 4 },
    barracks: { x: 50, y: 70, w: 13, z: 5 }
  },
  wall: {
    arrowTower: { x: 18, y: 26, w: 15, z: 2 },
    gate: { x: 50, y: 72, w: 18, z: 5 },
    trap: { x: 82, y: 44, w: 14, z: 3 }
  }
};

const selectedType = ref<string | null>(null);
const phase = computed(() => dayPhase(props.nowMs));

const plots = computed(() =>
  props.items.map((item) => {
    const pos = layouts[props.mode][item.type] ?? { x: 50, y: 50, w: 12, z: 3 };
    return { item, pos };
  })
);

const selected = computed(() => props.items.find((item) => item.type === selectedType.value) ?? null);

function pick(type: string): void {
  selectedType.value = type;
  emit("pick");
}

function broken(ev: Event): void {
  (ev.target as HTMLImageElement).classList.add("missing");
}
</script>

<template>
  <div class="scene-shell">
    <div class="scene" :class="[mode, phase, { threatened }]">
      <div class="sky"></div>
      <div class="ridge"></div>
      <div class="haze"></div>
      <div class="ground" :class="{ stone: mode === 'wall' }"></div>
      <div v-if="mode === 'wall'" class="moat"></div>
      <div v-if="mode === 'wall'" class="rampart"></div>
      <div class="plaza"></div>
      <div class="wall-band left"></div>
      <div class="wall-band right"></div>
      <div class="wall-band top"></div>
      <div class="gate-road"></div>
      <i class="bird a"></i>
      <i class="bird b"></i>
      <i v-if="mode === 'inner'" class="walker a"></i>
      <i v-if="mode === 'inner'" class="walker b"></i>
      <i v-if="mode === 'wall'" class="sentry a"></i>
      <i v-if="mode === 'wall'" class="sentry b"></i>
      <i class="banner left"></i>
      <i class="banner right"></i>
      <i v-if="mode === 'wall'" class="torch a"></i>
      <i v-if="mode === 'wall'" class="torch b"></i>
      <i v-for="n in 8" :key="n" class="firefly" :style="{ '--i': n }"></i>
      <button
        v-for="plot in plots"
        :key="plot.item.type"
        type="button"
        class="plot"
        :class="{
          empty: plot.item.level < 1,
          upgrading: plot.item.status === 'upgrading',
          selected: selectedType === plot.item.type,
          locked: Boolean(plot.item.blockedReason) && plot.item.level < 1
        }"
        :style="{
          left: plot.pos.x + '%',
          top: plot.pos.y + '%',
          width: plot.pos.w + '%',
          zIndex: plot.pos.z
        }"
        @click="pick(plot.item.type)"
      >
        <span class="shadow"></span>
        <img :src="buildingPortrait(plot.item.type)" :alt="plot.item.name" @error="broken" />
        <span class="scaffold" v-if="plot.item.status === 'upgrading'"></span>
        <span class="dust" v-if="plot.item.status === 'upgrading'"></span>
        <span class="smoke" v-if="plot.item.level >= 1 && plot.item.status !== 'upgrading'"></span>
        <b class="name">{{ plot.item.name }}</b>
        <em class="lv">{{ plot.item.level }}/{{ plot.item.maxLevel }}</em>
      </button>
    </div>

    <aside v-if="selected" class="plot-dock">
      <div class="portrait">
        <img :src="buildingPortrait(selected.type)" :alt="selected.name" />
        <span class="lv">{{ selected.level }}/{{ selected.maxLevel }}</span>
      </div>
      <div class="info">
        <strong>{{ selected.name }}</strong>
        <div class="level-bar"><i :style="{ width: `${(selected.level / selected.maxLevel) * 100}%` }"></i></div>
        <span v-if="effectsText(selected.effects)" class="hint">{{ effectsText(selected.effects) }}</span>
        <span v-if="selected.status === 'upgrading'" class="hint pulse">
          工匠施工中 {{ remainText(selected.finishAt, nowMs) }}
        </span>
        <span v-else-if="blockedText(selected.blockedReason)" class="hint">{{
          blockedText(selected.blockedReason)
        }}</span>
        <div v-if="selected.next" class="cost-row">
          <span v-for="part in costParts(selected.next)" :key="part.key" class="cost-chip">
            <img :src="resourceArt[part.key]" :alt="resourceLabel[part.key]" />
            {{ part.amount }}
          </span>
          <span class="cost-chip time">{{ selected.next.durationSeconds }}秒</span>
        </div>
      </div>
      <div class="card-actions">
        <button
          type="button"
          :disabled="busy || selected.status === 'upgrading' || Boolean(selected.blockedReason)"
          @click="emit('upgrade', selected.type)"
        >
          {{ selected.level === 0 ? "建造" : "升级" }}
        </button>
      </div>
    </aside>
    <p v-else class="hint scene-tip">
      {{ mode === "wall" ? "点选箭塔、城门或陷阱，可加固城防。" : "点选城中建筑，可查看效果并下令建造。" }}
    </p>
    <p v-if="mode === 'wall'" class="hint scene-tip">
      守城 {{ wallDefense ?? 0 }} · 陷阱加成 {{ Math.round((trapBonus ?? 0) * 100) }}%
      <template v-if="threatened"> · 敌军压境，守军已上墙</template>
    </p>
  </div>
</template>
