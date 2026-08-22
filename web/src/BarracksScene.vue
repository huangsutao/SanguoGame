<script setup lang="ts">
import { computed, watch } from "vue";
import type { ArmyOverviewDto } from "./api/types";
import { resourceArt, resourceKeys, troopPortrait } from "./art";
import { dayPhase, remainText, resourceLabel, troopLabel } from "./format";

const props = defineProps<{
  army: ArmyOverviewDto;
  busy?: boolean;
  nowMs: number;
  troopType: string;
  count: number;
}>();

const emit = defineEmits<{
  "update:troopType": [value: string];
  "update:count": [value: number];
  recruit: [];
  pick: [];
}>();

const layout: Record<string, { x: number; y: number; w: number; z: number }> = {
  infantry: { x: 22, y: 56, w: 16, z: 4 },
  archer: { x: 50, y: 46, w: 16, z: 3 },
  cavalry: { x: 78, y: 56, w: 16, z: 4 }
};

const phase = computed(() => dayPhase(props.nowMs));

const plots = computed(() =>
  props.army.troopTypes.map((item) => ({
    item,
    pos: layout[item.type] ?? { x: 50, y: 52, w: 14, z: 3 },
    count: props.army.troops[item.type as keyof typeof props.army.troops] ?? 0,
    recruiting: props.army.recruitQueue?.troopType === item.type,
    locked: props.army.barracksLevel < item.requireBarracksLevel
  }))
);

const selected = computed(() => plots.value.find((plot) => plot.item.type === props.troopType) ?? plots.value[0] ?? null);

const recruitAmount = computed(() => Math.max(1, Math.min(100, Math.floor(Number(props.count) || 1))));

watch(
  () => props.army.troopTypes[0]?.type,
  (type) => {
    if (type && !props.army.troopTypes.some((item) => item.type === props.troopType)) {
      emit("update:troopType", type);
    }
  },
  { immediate: true }
);

function pick(type: string): void {
  emit("update:troopType", type);
  emit("pick");
}

function broken(ev: Event): void {
  (ev.target as HTMLImageElement).classList.add("missing");
}
</script>

<template>
  <div class="scene-shell">
    <div class="scene army" :class="phase">
      <div class="sky"></div>
      <div class="ridge"></div>
      <div class="haze"></div>
      <div class="ground"></div>
      <div class="plaza drill-yard"></div>
      <div class="wall-band left"></div>
      <div class="wall-band right"></div>
      <div class="gate-road"></div>
      <i class="bird a"></i>
      <i class="bird b"></i>
      <i class="walker a"></i>
      <i class="walker b"></i>
      <i class="banner left"></i>
      <i class="banner right"></i>
      <i class="drill-dust a"></i>
      <i class="drill-dust b"></i>
      <i v-for="n in 8" :key="n" class="firefly" :style="{ '--i': n }"></i>
      <div class="plot landmark barracks-mark">
        <span class="shadow"></span>
        <img src="/art/barracks.jpg" alt="兵营" />
        <b class="name">兵营 {{ army.barracksLevel }} 级</b>
      </div>
      <div class="map-hud">
        <span>驻军 {{ army.troops.infantry + army.troops.archer + army.troops.cavalry }}/{{ army.troopCap }}</span>
        <span>城防 {{ army.wallDefense }}</span>
        <span v-if="army.troopPowerBonusPercent">战力+{{ army.troopPowerBonusPercent }}%</span>
      </div>
      <button
        v-for="plot in plots"
        :key="plot.item.type"
        type="button"
        class="plot troop-plot"
        :class="{
          selected: troopType === plot.item.type,
          locked: plot.locked,
          upgrading: plot.recruiting
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
        <img :src="troopPortrait(plot.item.type)" :alt="plot.item.name" @error="broken" />
        <span class="scaffold" v-if="plot.recruiting"></span>
        <span class="dust" v-if="plot.recruiting"></span>
        <b class="name">{{ plot.item.name }}</b>
        <em class="lv">{{ plot.count }}</em>
        <span v-if="plot.recruiting && army.recruitQueue" class="ripe-tag">
          训练 {{ army.recruitQueue.count }}
        </span>
      </button>
    </div>

    <aside v-if="selected" class="plot-dock">
      <div class="portrait">
        <img :src="troopPortrait(selected.item.type)" :alt="selected.item.name" />
        <span class="lv">{{ selected.count }}</span>
      </div>
      <div class="info">
        <strong>{{ selected.item.name }}</strong>
        <span class="hint">兵营 ≥ {{ selected.item.requireBarracksLevel }} 级 · 现有 {{ selected.count }}</span>
        <span v-if="selected.locked" class="hint">兵营等级不足，无法征召。</span>
        <span v-else-if="army.recruitQueue" class="hint pulse">
          征兵中 {{ troopLabel[army.recruitQueue.troopType] ?? army.recruitQueue.troopType }}
          × {{ army.recruitQueue.count }}，剩余 {{ remainText(army.recruitQueue.finishAt, nowMs) }}
        </span>
        <span v-else class="hint">下达后扣资源，到点入帐。步兵 1 级、弓兵 2 级、骑兵 3 级。</span>
        <div class="cost-row">
          <span v-for="key in resourceKeys" :key="key" class="cost-chip">
            <img :src="resourceArt[key]" :alt="resourceLabel[key]" />
            {{ selected.item.unitCost[key] * recruitAmount }}
          </span>
          <span v-if="army.recruitDiscountPercent" class="cost-chip time">减免 {{ army.recruitDiscountPercent }}%</span>
        </div>
      </div>
      <div class="card-actions shop-actions">
        <input
          :value="count"
          type="number"
          min="1"
          max="100"
          @input="emit('update:count', Number(($event.target as HTMLInputElement).value))"
        />
        <button type="button" :disabled="busy || Boolean(army.recruitQueue) || selected.locked" @click="emit('recruit')">
          征兵
        </button>
      </div>
    </aside>
    <p v-else class="hint scene-tip">点选校场兵种，可查看消耗并下令征召。</p>
  </div>
</template>
