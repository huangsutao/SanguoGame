<script setup lang="ts">
import { onMounted, onUnmounted, ref } from "vue";
import type { WorldDto } from "./api/types";
import { markerArt } from "./art";

const props = defineProps<{ world: WorldDto }>();
const emit = defineEmits<{
  select: [target: { targetType: "outpost" | "city" | "market"; targetId: number; label: string }];
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
let raf = 0;

const sprites = {
  city: loadSprite(markerArt.city),
  outpost: loadSprite(markerArt.outpost),
  roaming: loadSprite(markerArt.roaming),
  market: loadSprite(markerArt.market),
  march: loadSprite(markerArt.march),
  march2: loadSprite(markerArt.march2),
  cart: loadSprite(markerArt.cart)
};

function loadSprite(src: string): HTMLImageElement {
  const img = new Image();
  img.src = src;
  return img;
}

function ready(img: HTMLImageElement): boolean {
  return img.complete && img.naturalWidth > 0;
}

function drawSprite(ctx: CanvasRenderingContext2D, img: HTMLImageElement, sx: number, sy: number, size: number): void {
  if (ready(img)) {
    ctx.drawImage(img, sx - size / 2, sy - size / 2, size, size);
    return;
  }
  ctx.fillStyle = "#c9a45a";
  ctx.beginPath();
  ctx.arc(sx, sy, size / 3, 0, Math.PI * 2);
  ctx.fill();
}

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
  const sky = ctx.createLinearGradient(0, 0, 0, h);
  sky.addColorStop(0, "#2a3824");
  sky.addColorStop(0.55, "#1c2418");
  sky.addColorStop(1, "#14120e");
  ctx.fillStyle = sky;
  ctx.fillRect(0, 0, w, h);
  const cell = scale.value;
  const toScreen = (x: number, y: number) => ({
    sx: w / 2 + (x - originX.value) * cell,
    sy: h / 2 + (y - originY.value) * cell
  });

  ctx.strokeStyle = "rgba(70, 90, 50, 0.35)";
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
  type Mover = {
    sx: number;
    sy: number;
    angle: number;
    mine: boolean;
    kind: "march" | "transport";
  };
  const movers: Mover[] = [];

  const pointOnPath = (
    t: number,
    fromX: number,
    fromY: number,
    toX: number,
    toY: number,
    roundTrip: boolean
  ) => {
    const clamped = Math.min(1, Math.max(0, t));
    if (!roundTrip) {
      return {
        x: fromX + (toX - fromX) * clamped,
        y: fromY + (toY - fromY) * clamped
      };
    }
    if (clamped < 0.5) {
      const u = clamped * 2;
      return { x: fromX + (toX - fromX) * u, y: fromY + (toY - fromY) * u };
    }
    const u = (clamped - 0.5) * 2;
    return { x: toX + (fromX - toX) * u, y: toY + (fromY - toY) * u };
  };

  const placeMover = (
    fromX: number,
    fromY: number,
    toX: number,
    toY: number,
    departAt: string,
    arriveAt: string,
    mine: boolean,
    roundTrip: boolean,
    kind: "march" | "transport",
    stroke: string
  ) => {
    const t = Math.min(
      1,
      Math.max(0, (now - Date.parse(departAt)) / Math.max(1, Date.parse(arriveAt) - Date.parse(departAt)))
    );
    const here = pointOnPath(t, fromX, fromY, toX, toY, roundTrip);
    const lookT = t >= 0.98 ? Math.max(0, t - 0.02) : t + 0.02;
    const look = pointOnPath(lookT, fromX, fromY, toX, toY, roundTrip);
    const from = toScreen(fromX, fromY);
    const to = toScreen(toX, toY);
    const cur = toScreen(here.x, here.y);
    const lookS = toScreen(look.x, look.y);
    const angle =
      t >= 0.98
        ? Math.atan2(cur.sy - lookS.sy, cur.sx - lookS.sx)
        : Math.atan2(lookS.sy - cur.sy, lookS.sx - cur.sx);
    ctx.save();
    ctx.strokeStyle = mine ? stroke : "#6a7a8a";
    ctx.lineWidth = 2;
    ctx.setLineDash([7, 6]);
    ctx.lineDashOffset = -((now / 35) % 26);
    ctx.beginPath();
    ctx.moveTo(from.sx, from.sy);
    ctx.lineTo(to.sx, to.sy);
    ctx.stroke();
    ctx.restore();
    movers.push({
      sx: cur.sx,
      sy: cur.sy,
      angle,
      mine,
      kind
    });
  };

  for (const item of props.world.marches) {
    placeMover(item.fromX, item.fromY, item.toX, item.toY, item.departAt, item.arriveAt, item.mine, false, "march", "#d4b46a");
  }
  for (const item of props.world.transports ?? []) {
    placeMover(
      item.fromX,
      item.fromY,
      item.toX,
      item.toY,
      item.departAt,
      item.arriveAt,
      item.mine,
      item.kind === "market",
      "transport",
      "#6bc4a8"
    );
  }

  const markSize = Math.max(16, Math.min(34, cell * 2.4));

  for (const item of props.world.outposts) {
    const p = toScreen(item.x, item.y);
    const roaming = item.kind === "roaming";
    ctx.globalAlpha = item.garrison > 0 ? 1 : 0.55;
    drawSprite(ctx, roaming ? sprites.roaming : sprites.outpost, p.sx, p.sy, markSize);
    ctx.globalAlpha = 1;
  }

  for (const item of props.world.markets ?? []) {
    const p = toScreen(item.x, item.y);
    drawSprite(ctx, sprites.market, p.sx, p.sy, markSize);
  }

  for (const item of props.world.cities) {
    const p = toScreen(item.x, item.y);
    const size = item.owner === "self" ? markSize + 6 : markSize;
    drawSprite(ctx, sprites.city, p.sx, p.sy, size);
    ctx.strokeStyle = item.owner === "self" ? "#d4a017" : item.owner === "ai" ? "#a34a36" : "#4a8a6a";
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(p.sx, p.sy, size / 2 + 2, 0, Math.PI * 2);
    ctx.stroke();
    if (item.protected) {
      ctx.strokeStyle = "#7aa0c4";
      ctx.beginPath();
      ctx.arc(p.sx, p.sy, size / 2 + 6, 0, Math.PI * 2);
      ctx.stroke();
    }
  }

  const unitSize = Math.max(20, Math.min(36, cell * 2.6));
  const walkFrame = Math.floor(now / 180) % 2;
  const bob = Math.sin(now / 130) * 2.4;
  for (const mover of movers) {
    const img =
      mover.kind === "transport" ? sprites.cart : walkFrame === 0 ? sprites.march : sprites.march2;
    ctx.fillStyle = "rgba(0, 0, 0, 0.35)";
    ctx.beginPath();
    ctx.ellipse(mover.sx, mover.sy + unitSize * 0.38, unitSize * 0.28, unitSize * 0.12, 0, 0, Math.PI * 2);
    ctx.fill();
    ctx.save();
    ctx.translate(mover.sx, mover.sy + bob);
    if (Math.cos(mover.angle) < 0) {
      ctx.scale(-1, 1);
    }
    ctx.globalAlpha = mover.mine ? 1 : 0.82;
    drawSprite(ctx, img, 0, 0, unitSize);
    ctx.restore();
    ctx.save();
    ctx.translate(
      mover.sx + Math.cos(mover.angle) * (unitSize * 0.58),
      mover.sy + bob + Math.sin(mover.angle) * (unitSize * 0.58)
    );
    ctx.rotate(mover.angle);
    ctx.fillStyle = mover.mine ? (mover.kind === "transport" ? "#8ee0c4" : "#e8c97a") : "#8aa0b4";
    ctx.beginPath();
    ctx.moveTo(7, 0);
    ctx.lineTo(-4, 5);
    ctx.lineTo(-4, -5);
    ctx.closePath();
    ctx.fill();
    ctx.restore();
  }
}

function hit(clientX: number, clientY: number): void {
  const el = canvas.value;
  if (!el) {
    return;
  }
  const rect = el.getBoundingClientRect();
  const mx = (clientX - rect.left) * (el.width / Math.max(1, rect.width));
  const my = (clientY - rect.top) * (el.height / Math.max(1, rect.height));
  const x = originX.value + (mx - el.width / 2) / scale.value;
  const y = originY.value + (my - el.height / 2) / scale.value;
  let best: { targetType: "outpost" | "city" | "market"; targetId: number; label: string; d: number } | null = null;
  const consider = (
    targetType: "outpost" | "city" | "market",
    targetId: number,
    label: string,
    px: number,
    py: number
  ) => {
    const d = Math.hypot(px - x, py - y);
    if (d <= 1.8 && (!best || d < best.d)) {
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
    let label = item.name;
    if (item.kind === "roaming" && item.expiresAt) {
      const left = Math.max(0, Date.parse(item.expiresAt) - Date.now());
      const min = Math.max(1, Math.ceil(left / 60000));
      label = `${item.name}（${min}分钟后消失）`;
    }
    consider("outpost", item.id, label, item.x, item.y);
  }
  for (const item of props.world.markets ?? []) {
    consider("market", item.id, item.name, item.x, item.y);
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
  raf = requestAnimationFrame(loop);
});

function loop(): void {
  draw();
  raf = requestAnimationFrame(loop);
}

onUnmounted(() => {
  cancelAnimationFrame(raf);
  dragging = false;
});
</script>

<template>
  <canvas
    ref="canvas"
    width="800"
    height="440"
    class="map"
    @pointerdown="onPointerDown"
    @pointermove="onPointerMove"
    @pointerup="onPointerUp"
    @wheel.prevent="onWheel"
  ></canvas>
</template>
