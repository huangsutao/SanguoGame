<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from "vue";
import type { WorldDto } from "./api/types";

const props = defineProps<{ world: WorldDto }>();
const emit = defineEmits<{
  select: [target: { targetType: "outpost" | "city"; targetId: number; label: string }];
}>();

const canvas = ref<HTMLCanvasElement | null>(null);
const originX = ref(props.world.origin.x);
const originY = ref(props.world.origin.y);
const scale = ref(8);
let dragging = false;
let lastX = 0;
let lastY = 0;
let startX = 0;
let startY = 0;

function draw(): void {
  const el = canvas.value;
  if (!el) {
    return;
  }
  const ctx = el.getContext("2d");
  if (!ctx) {
    return;
  }
  const w = el.width;
  const h = el.height;
  ctx.fillStyle = "#1a1612";
  ctx.fillRect(0, 0, w, h);
  const cell = scale.value;
  const toScreen = (x: number, y: number) => ({
    sx: w / 2 + (x - originX.value) * cell,
    sy: h / 2 + (y - originY.value) * cell
  });

  ctx.strokeStyle = "#2a251f";
  ctx.lineWidth = 1;
  const minX = Math.floor(originX.value - w / 2 / cell) - 1;
  const maxX = Math.ceil(originX.value + w / 2 / cell) + 1;
  const minY = Math.floor(originY.value - h / 2 / cell) - 1;
  const maxY = Math.ceil(originY.value + h / 2 / cell) + 1;
  if (cell >= 10) {
    for (let x = minX; x <= maxX; x++) {
      const a = toScreen(x, minY);
      const b = toScreen(x, maxY);
      ctx.beginPath();
      ctx.moveTo(a.sx, a.sy);
      ctx.lineTo(b.sx, b.sy);
      ctx.stroke();
    }
    for (let y = minY; y <= maxY; y++) {
      const a = toScreen(minX, y);
      const b = toScreen(maxX, y);
      ctx.beginPath();
      ctx.moveTo(a.sx, a.sy);
      ctx.lineTo(b.sx, b.sy);
      ctx.stroke();
    }
  }

  const now = Date.now();
  ctx.lineWidth = 2;
  for (const item of props.world.marches) {
    const t = Math.min(
      1,
      Math.max(0, (now - Date.parse(item.departAt)) / Math.max(1, Date.parse(item.arriveAt) - Date.parse(item.departAt)))
    );
    const ax = item.fromX + (item.toX - item.fromX) * t;
    const ay = item.fromY + (item.toY - item.fromY) * t;
    const from = toScreen(item.fromX, item.fromY);
    const to = toScreen(item.toX, item.toY);
    const cur = toScreen(ax, ay);
    ctx.strokeStyle = item.mine ? "#d4b46a" : "#6a7a8a";
    ctx.beginPath();
    ctx.moveTo(from.sx, from.sy);
    ctx.lineTo(to.sx, to.sy);
    ctx.stroke();
    ctx.fillStyle = item.mine ? "#e8c97a" : "#8aa0b4";
    ctx.beginPath();
    ctx.arc(cur.sx, cur.sy, 3, 0, Math.PI * 2);
    ctx.fill();
  }

  for (const item of props.world.outposts) {
    const p = toScreen(item.x, item.y);
    ctx.fillStyle = item.garrison > 0 ? "#6b5b3a" : "#3a342a";
    ctx.fillRect(p.sx - 4, p.sy - 4, 8, 8);
  }

  for (const item of props.world.cities) {
    const p = toScreen(item.x, item.y);
    ctx.fillStyle = item.owner === "self" ? "#d4a017" : item.owner === "ai" ? "#a34a36" : "#4a8a6a";
    ctx.beginPath();
    ctx.arc(p.sx, p.sy, 6, 0, Math.PI * 2);
    ctx.fill();
    if (item.protected) {
      ctx.strokeStyle = "#7aa0c4";
      ctx.stroke();
    }
  }
}

function hit(clientX: number, clientY: number): void {
  const el = canvas.value;
  if (!el) {
    return;
  }
  const rect = el.getBoundingClientRect();
  const x = originX.value + (clientX - rect.left - el.width / 2) / scale.value;
  const y = originY.value + (clientY - rect.top - el.height / 2) / scale.value;
  let best: { targetType: "outpost" | "city"; targetId: number; label: string; d: number } | null = null;
  const consider = (targetType: "outpost" | "city", targetId: number, label: string, px: number, py: number) => {
    const d = Math.hypot(px - x, py - y);
    if (d <= 1.2 && (!best || d < best.d)) {
      best = { targetType, targetId, label, d };
    }
  };
  for (const item of props.world.cities) {
    if (item.owner === "self") {
      continue;
    }
    consider("city", item.id, `${item.name} (${item.x},${item.y})`, item.x, item.y);
  }
  for (const item of props.world.outposts) {
    consider("outpost", item.id, `${item.name}`, item.x, item.y);
  }
  if (best) {
    emit("select", { targetType: best.targetType, targetId: best.targetId, label: best.label });
  }
}

function onPointerDown(ev: PointerEvent): void {
  dragging = true;
  lastX = ev.clientX;
  lastY = ev.clientY;
  startX = ev.clientX;
  startY = ev.clientY;
  (ev.target as HTMLElement).setPointerCapture(ev.pointerId);
}

function onPointerMove(ev: PointerEvent): void {
  if (!dragging) {
    return;
  }
  originX.value -= (ev.clientX - lastX) / scale.value;
  originY.value -= (ev.clientY - lastY) / scale.value;
  lastX = ev.clientX;
  lastY = ev.clientY;
  draw();
}

function onPointerUp(ev: PointerEvent): void {
  if (Math.hypot(ev.clientX - startX, ev.clientY - startY) < 6) {
    hit(ev.clientX, ev.clientY);
  }
  dragging = false;
}

function onWheel(ev: WheelEvent): void {
  ev.preventDefault();
  const next = Math.min(24, Math.max(3, scale.value * (ev.deltaY > 0 ? 0.9 : 1.1)));
  scale.value = next;
  draw();
}

onMounted(() => {
  draw();
});

watch(
  () => props.world,
  () => {
    draw();
  },
  { deep: true }
);

onUnmounted(() => {
  dragging = false;
});
</script>

<template>
  <canvas
    ref="canvas"
    width="600"
    height="360"
    class="map"
    @pointerdown="onPointerDown"
    @pointermove="onPointerMove"
    @pointerup="onPointerUp"
    @wheel.prevent="onWheel"
  ></canvas>
</template>
