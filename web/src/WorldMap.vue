<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from "vue";
import type { WorldDto } from "./api/types";
import { markerArt } from "./art";

const props = withDefaults(
  defineProps<{ world: WorldDto; active?: boolean }>(),
  { active: true }
);
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
let snapshotAt = Date.now();
let snapshotServer = Date.parse(props.world.serverTime);

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
  img.onload = () => draw();
  return img;
}

function ready(img: HTMLImageElement): boolean {
  return img.complete && img.naturalWidth > 0;
}

function serverNow(): number {
  const parsed = Number.isFinite(snapshotServer) ? snapshotServer : Date.now();
  return parsed + (Date.now() - snapshotAt);
}

function markSize(): number {
  return Math.max(16, Math.min(34, scale.value * 2.4));
}

function hitRadius(): number {
  return Math.max(1.4, (markSize() / 2 + 10) / scale.value);
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

function needsAnim(): boolean {
  return (props.world.marches?.length ?? 0) + (props.world.transports?.length ?? 0) > 0;
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

  const now = serverNow();
  type Mover = {
    sx: number;
    sy: number;
    angle: number;
    mine: boolean;
    kind: "march" | "scout" | "transport";
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
    kind: "march" | "scout" | "transport",
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
    placeMover(
      item.fromX,
      item.fromY,
      item.toX,
      item.toY,
      item.departAt,
      item.arriveAt,
      item.mine,
      false,
      item.kind === "scout" ? "scout" : "march",
      item.kind === "scout" ? "#7eb6d9" : "#d4b46a"
    );
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

  const size = markSize();

  for (const item of props.world.outposts) {
    const p = toScreen(item.x, item.y);
    const roaming = item.kind === "roaming";
    ctx.globalAlpha = item.garrison > 0 ? 1 : 0.55;
    drawSprite(ctx, roaming ? sprites.roaming : sprites.outpost, p.sx, p.sy, size);
    ctx.globalAlpha = 1;
  }

  for (const item of props.world.markets ?? []) {
    const p = toScreen(item.x, item.y);
    drawSprite(ctx, sprites.market, p.sx, p.sy, size);
  }

  for (const item of props.world.cities) {
    const p = toScreen(item.x, item.y);
    const citySize = item.owner === "self" ? size + 6 : size;
    drawSprite(ctx, sprites.city, p.sx, p.sy, citySize);
    ctx.strokeStyle = item.owner === "self" ? "#d4a017" : item.owner === "ai" ? "#a34a36" : "#4a8a6a";
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.arc(p.sx, p.sy, citySize / 2 + 2, 0, Math.PI * 2);
    ctx.stroke();
    if (item.protected) {
      ctx.strokeStyle = "#7aa0c4";
      ctx.beginPath();
      ctx.arc(p.sx, p.sy, citySize / 2 + 6, 0, Math.PI * 2);
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
    ctx.fillStyle = mover.mine
      ? mover.kind === "transport"
        ? "#8ee0c4"
        : mover.kind === "scout"
          ? "#9fd0ea"
          : "#e8c97a"
      : "#8aa0b4";
    ctx.beginPath();
    ctx.moveTo(7, 0);
    ctx.lineTo(-4, 5);
    ctx.lineTo(-4, -5);
    ctx.closePath();
    ctx.fill();
    ctx.restore();
  }
}

function canvasPoint(clientX: number, clientY: number): { mx: number; my: number } | null {
  const el = canvas.value;
  if (!el) {
    return null;
  }
  const rect = el.getBoundingClientRect();
  return {
    mx: (clientX - rect.left) * (el.width / Math.max(1, rect.width)),
    my: (clientY - rect.top) * (el.height / Math.max(1, rect.height))
  };
}

function hit(clientX: number, clientY: number): void {
  const el = canvas.value;
  const pt = canvasPoint(clientX, clientY);
  if (!el || !pt) {
    return;
  }
  const x = originX.value + (pt.mx - el.width / 2) / scale.value;
  const y = originY.value + (pt.my - el.height / 2) / scale.value;
  const radius = hitRadius();
  let best: { targetType: "outpost" | "city" | "market"; targetId: number; label: string; d: number } | null = null;
  const consider = (
    targetType: "outpost" | "city" | "market",
    targetId: number,
    label: string,
    px: number,
    py: number
  ) => {
    const d = Math.hypot(px - x, py - y);
    if (d <= radius && (!best || d < best.d)) {
      best = { targetType, targetId, label, d };
    }
  };
  for (const item of props.world.cities) {
    if (item.owner === "self") {
      continue;
    }
    const owner = item.owner === "ai" ? "AI" : "玩家";
    const shield = item.protected ? " · 保护中" : "";
    consider("city", item.id, `${item.name}（${owner}${shield}）(${item.x},${item.y})`, item.x, item.y);
  }
  for (const item of props.world.outposts) {
    let label = item.name;
    if (item.kind === "roaming" && item.expiresAt) {
      const left = Math.max(0, Date.parse(item.expiresAt) - serverNow());
      const min = Math.max(1, Math.ceil(left / 60000));
      label = `${item.name}（${min}分钟后消失）`;
    }
    if (item.garrison <= 0) {
      label = `${label}（已打空）`;
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
  const el = canvas.value;
  const pt = canvasPoint(ev.clientX, ev.clientY);
  if (!el || !pt) {
    return;
  }
  const worldX = originX.value + (pt.mx - el.width / 2) / scale.value;
  const worldY = originY.value + (pt.my - el.height / 2) / scale.value;
  const next = Math.min(24, Math.max(3, scale.value * (ev.deltaY > 0 ? 0.9 : 1.1)));
  scale.value = next;
  originX.value = worldX - (pt.mx - el.width / 2) / next;
  originY.value = worldY - (pt.my - el.height / 2) / next;
  draw();
}

function recenter(): void {
  originX.value = props.world.origin.x;
  originY.value = props.world.origin.y;
  scale.value = 8;
  draw();
}

function stopLoop(): void {
  if (raf) {
    cancelAnimationFrame(raf);
    raf = 0;
  }
}

function loop(): void {
  draw();
  if (props.active && needsAnim()) {
    raf = requestAnimationFrame(loop);
  } else {
    raf = 0;
  }
}

function startLoop(): void {
  if (!props.active || raf) {
    return;
  }
  raf = requestAnimationFrame(loop);
}

watch(
  () => props.world,
  (world) => {
    snapshotAt = Date.now();
    snapshotServer = Date.parse(world.serverTime);
    draw();
    if (props.active && needsAnim()) {
      startLoop();
    }
  }
);

watch(
  () => props.active,
  (on) => {
    if (on) {
      draw();
      startLoop();
      return;
    }
    stopLoop();
  }
);

onMounted(() => {
  draw();
  startLoop();
});

onUnmounted(() => {
  stopLoop();
  dragging = false;
});
</script>

<template>
  <div class="map-wrap">
    <canvas
      ref="canvas"
      width="800"
      height="440"
      class="map"
      @pointerdown="onPointerDown"
      @pointermove="onPointerMove"
      @pointerup="onPointerUp"
      @pointercancel="onPointerUp"
      @wheel.prevent="onWheel"
    ></canvas>
    <button type="button" class="map-home" @click="recenter">回城</button>
  </div>
</template>
