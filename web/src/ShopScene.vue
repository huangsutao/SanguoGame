<script setup lang="ts">
import { computed, ref } from "vue";
import type { ShopCatalogItemDto, ShopOverviewDto } from "./api/types";
import { shopPortrait } from "./art";
import { dayPhase, remainText } from "./format";

const props = defineProps<{
  shop: ShopOverviewDto;
  busy?: boolean;
  nowMs: number;
  relocateX: number;
  relocateY: number;
  mapWidth?: number;
  mapHeight?: number;
}>();

const emit = defineEmits<{
  "update:relocateX": [value: number];
  "update:relocateY": [value: number];
  buy: [item: ShopCatalogItemDto, count: number];
  use: [item: ShopCatalogItemDto, count: number];
  pick: [];
}>();

const layout: Record<string, { x: number; y: number; w: number; z: number }> = {
  speedBuild: { x: 16, y: 42, w: 12, z: 3 },
  speedUpgrade: { x: 33, y: 36, w: 12, z: 3 },
  speedTech: { x: 50, y: 28, w: 13, z: 2 },
  speedRecruit: { x: 67, y: 36, w: 12, z: 3 },
  resourceBoost: { x: 84, y: 44, w: 12, z: 3 },
  queueBuild: { x: 16, y: 58, w: 11, z: 4 },
  queueField: { x: 39, y: 58, w: 11, z: 4 },
  queueTech: { x: 61, y: 58, w: 11, z: 4 },
  queueRecruit: { x: 84, y: 58, w: 11, z: 4 },
  relocateRandom: { x: 32, y: 78, w: 13, z: 5 },
  relocateTarget: { x: 68, y: 78, w: 13, z: 5 }
};

const selectedType = ref<string | null>(null);
const buyCount = ref(1);
const phase = computed(() => dayPhase(props.nowMs));

const plots = computed(() =>
  props.shop.catalog.map((item) => ({
    item,
    pos: layout[item.type] ?? { x: 50, y: 50, w: 12, z: 3 },
    buff: props.shop.buffs.find((buff) => buff.type === item.type)
  }))
);

const selected = computed(() => plots.value.find((plot) => plot.item.type === selectedType.value) ?? null);

const count = computed(() => Math.max(1, Math.min(99, Math.floor(Number(buyCount.value) || 1))));

function kindLabel(kind: string): string {
  if (kind === "buff") {
    return "时效令";
  }
  if (kind === "unlock") {
    return "永久令";
  }
  return "消耗品";
}

function extraAlready(type: string): boolean {
  const slots = props.shop.slots;
  if (!slots) {
    return false;
  }
  switch (type) {
    case "queueBuild":
      return slots.build.extra >= 1;
    case "queueField":
      return slots.field.extra >= 1;
    case "queueTech":
      return slots.tech.extra >= 1;
    case "queueRecruit":
      return slots.recruit.extra >= 1;
    default:
      return false;
  }
}

function pick(type: string): void {
  selectedType.value = type;
  buyCount.value = 1;
  emit("pick");
}

function broken(ev: Event): void {
  (ev.target as HTMLImageElement).classList.add("missing");
}
</script>

<template>
  <div class="scene-shell">
    <div class="scene shop" :class="phase">
      <div class="sky"></div>
      <div class="ridge"></div>
      <div class="haze"></div>
      <div class="ground"></div>
      <div class="plaza market-street"></div>
      <div class="wall-band left"></div>
      <div class="wall-band right"></div>
      <div class="gate-road"></div>
      <i class="bird a"></i>
      <i class="bird b"></i>
      <i class="walker a"></i>
      <i class="walker farmer"></i>
      <i class="banner left"></i>
      <i class="banner right"></i>
      <i class="lantern a"></i>
      <i class="lantern b"></i>
      <i v-for="n in 8" :key="n" class="firefly" :style="{ '--i': n }"></i>
      <div class="plot landmark market-mark">
        <span class="shadow"></span>
        <img src="/art/market.jpg" alt="商铺" />
      </div>
      <div class="map-hud">
        <span>元宝 {{ shop.yuanbao }}</span>
        <span v-if="shop.slots">建造 {{ shop.slots.build.used }}/{{ shop.slots.build.limit }}</span>
        <span v-if="shop.slots">资源 {{ shop.slots.field.used }}/{{ shop.slots.field.limit }}</span>
        <span v-if="shop.slots">科技 {{ shop.slots.tech.used }}/{{ shop.slots.tech.limit }}</span>
        <span v-if="shop.slots">征兵 {{ shop.slots.recruit.used }}/{{ shop.slots.recruit.limit }}</span>
        <span v-for="buff in shop.buffs" :key="buff.type">
          {{ buff.name }} {{ remainText(buff.expireAt, nowMs) }}
        </span>
      </div>
      <button
        v-for="plot in plots"
        :key="plot.item.type"
        type="button"
        class="plot stall"
        :class="{
          selected: selectedType === plot.item.type,
          active: Boolean(plot.buff),
          owned: plot.item.owned > 0
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
        <img :src="shopPortrait(plot.item.type)" :alt="plot.item.name" @error="broken" />
        <b class="name">{{ plot.item.name }}</b>
        <em class="lv">{{ plot.item.price }}宝</em>
        <span v-if="plot.item.owned > 0" class="ripe-tag">持有 {{ plot.item.owned }}</span>
      </button>
    </div>

    <aside v-if="selected" class="plot-dock">
      <div class="portrait">
        <img :src="shopPortrait(selected.item.type)" :alt="selected.item.name" />
        <span class="lv">{{ selected.item.owned }}</span>
      </div>
      <div class="info">
        <strong>{{ selected.item.name }}</strong>
        <span class="hint">{{ selected.item.price }} 元宝 · {{ kindLabel(selected.item.kind) }}</span>
        <span class="hint">{{ selected.item.description }}</span>
        <span v-if="selected.buff" class="hint pulse">
          生效中 {{ remainText(selected.buff.expireAt, nowMs) }}（+{{ selected.buff.speedPercent }}%）
        </span>
        <span v-else-if="extraAlready(selected.item.type)" class="hint">该队列已扩充，无需再用。</span>
        <div v-if="selected.item.type === 'relocateTarget'" class="form inline shop-coords">
          <label>目标 X <input :value="relocateX" type="number" min="0" :max="Math.max(0, (mapWidth ?? 200) - 1)" @input="emit('update:relocateX', Number(($event.target as HTMLInputElement).value) || 0)" /></label>
          <label>目标 Y <input :value="relocateY" type="number" min="0" :max="Math.max(0, (mapHeight ?? 200) - 1)" @input="emit('update:relocateY', Number(($event.target as HTMLInputElement).value) || 0)" /></label>
        </div>
      </div>
      <div class="card-actions shop-actions">
        <input v-model.number="buyCount" type="number" min="1" max="99" />
        <button
          type="button"
          :disabled="busy || shop.yuanbao < selected.item.price * count"
          @click="emit('buy', selected.item, count)"
        >
          购买
        </button>
        <button type="button" :disabled="busy || selected.item.owned < 1 || extraAlready(selected.item.type)" @click="emit('use', selected.item, count)">
          使用
        </button>
      </div>
    </aside>
    <p v-else class="hint scene-tip">点选铺面查看令牌。加速与丰收持续 5 小时；队列令可永久多开 1 条。元宝由出征掠夺获得。</p>
  </div>
</template>
