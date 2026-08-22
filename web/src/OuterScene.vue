<script setup lang="ts">
import { computed, ref } from "vue";
import type { FieldItemDto } from "./api/types";
import { buildingPortrait, resourceArt } from "./art";
import { blockedText, costParts, dayPhase, liveFieldPending, remainText, resourceLabel } from "./format";

const props = defineProps<{
  items: FieldItemDto[];
  busy?: boolean;
  nowMs: number;
}>();

const emit = defineEmits<{
  upgrade: [type: string];
  collect: [type?: string];
  pick: [];
}>();

const layout: Record<string, { x: number; y: number; w: number; z: number }> = {
  lumber: { x: 20, y: 30, w: 15, z: 2 },
  farm: { x: 40, y: 58, w: 16, z: 4 },
  ironMine: { x: 72, y: 34, w: 14, z: 2 },
  copperMine: { x: 80, y: 64, w: 14, z: 5 }
};

const selectedType = ref<string | null>(null);
const phase = computed(() => dayPhase(props.nowMs));

const plots = computed(() =>
  props.items.map((item) => {
    const pos = layout[item.type] ?? { x: 50, y: 50, w: 13, z: 3 };
    const pending = liveFieldPending(item, props.nowMs);
    return { item, pos, pending, fill: item.fieldCap > 0 ? Math.min(100, (pending / item.fieldCap) * 100) : 0 };
  })
);

const selected = computed(() => plots.value.find((plot) => plot.item.type === selectedType.value) ?? null);

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
    <div class="scene outer" :class="phase">
      <div class="sky"></div>
      <div class="ridge"></div>
      <div class="haze"></div>
      <div class="ground wild"></div>
      <div class="crop"></div>
      <div class="woods"></div>
      <div class="hills"></div>
      <i class="bird a"></i>
      <i class="bird b"></i>
      <i class="walker farmer"></i>
      <i class="leaf a"></i>
      <i class="leaf b"></i>
      <i class="leaf c"></i>
      <button
        v-for="plot in plots"
        :key="plot.item.type"
        type="button"
        class="plot field"
        :class="{
          empty: plot.item.level < 1,
          upgrading: plot.item.status === 'upgrading',
          selected: selectedType === plot.item.type,
          ripe: plot.pending > 0
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
        <span class="sway" v-if="plot.item.type === 'farm' && plot.item.level >= 1"></span>
        <b class="name">{{ plot.item.name }}</b>
        <em class="lv">{{ plot.item.level }}/{{ plot.item.maxLevel }}</em>
        <span v-if="plot.pending > 0" class="ripe-tag">可收 {{ plot.pending }}</span>
        <span v-if="plot.item.level >= 1" class="store-bar"><i :style="{ width: plot.fill + '%' }"></i></span>
      </button>
    </div>

    <div class="scene-actions">
      <button type="button" :disabled="busy" @click="emit('collect')">一键收取</button>
    </div>

    <aside v-if="selected" class="plot-dock">
      <div class="portrait">
        <img :src="buildingPortrait(selected.item.type)" :alt="selected.item.name" />
        <span class="lv">{{ selected.item.level }}/{{ selected.item.maxLevel }}</span>
      </div>
      <div class="info">
        <strong>{{ selected.item.name }}</strong>
        <div class="level-bar"><i :style="{ width: `${(selected.item.level / selected.item.maxLevel) * 100}%` }"></i></div>
        <span v-if="selected.item.level >= 1" class="hint">{{ selected.item.ratePerHour }}/时 · 田容 {{ selected.item.fieldCap }}</span>
        <span v-if="selected.pending > 0" class="pending-chip">
          可收 {{ selected.pending }} / {{ selected.item.fieldCap }} {{ resourceLabel[selected.item.resource] }}
        </span>
        <span v-if="selected.item.status === 'upgrading'" class="hint pulse">
          工匠施工中 {{ remainText(selected.item.finishAt, nowMs) }}
        </span>
        <span v-else-if="blockedText(selected.item.blockedReason)" class="hint">{{
          blockedText(selected.item.blockedReason)
        }}</span>
        <div v-if="selected.item.next" class="cost-row">
          <span v-for="part in costParts(selected.item.next)" :key="part.key" class="cost-chip">
            <img :src="resourceArt[part.key]" :alt="resourceLabel[part.key]" />
            {{ part.amount }}
          </span>
          <span class="cost-chip time">{{ selected.item.next.durationSeconds }}秒</span>
        </div>
      </div>
      <div class="card-actions">
        <button type="button" :disabled="busy || selected.item.level < 1" @click="emit('collect', selected.item.type)">
          收取
        </button>
        <button
          type="button"
          :disabled="busy || selected.item.status === 'upgrading' || Boolean(selected.item.blockedReason)"
          @click="emit('upgrade', selected.item.type)"
        >
          {{ selected.item.level === 0 ? "建造" : "升级" }}
        </button>
      </div>
    </aside>
    <p v-else class="hint scene-tip">点选田地查看产出；可收数量会随时间在画面上增长。</p>
  </div>
</template>
