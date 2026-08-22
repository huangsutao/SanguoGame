<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import type { WorldDto } from "./api/types";
import { markerArt } from "./art";
import { dayPhase, dayPhaseLabel, incomingOnCity, remainText } from "./format";

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
const scale = ref(10);
const hover = ref("");
const hudClock = ref(Date.now());
let dragging = false;
let lastX = 0;
let lastY = 0;
let startX = 0;
let startY = 0;
let raf = 0;
let snapshotAt = Date.now();
let snapshotServer = Date.parse(props.world.serverTime);
let pointerMx = -1;
let pointerMy = -1;
let lastHud = 0;

const sprites = {
  city: loadSprite(markerArt.city),
  outpost: loadSprite(markerArt.outpost),
  roaming: loadSprite(markerArt.roaming),
  market: loadSprite(markerArt.market),
  march: loadSprite(markerArt.march),
  march2: loadSprite(markerArt.march2),
  cart: loadSprite(markerArt.cart)
};

const phase = computed(() => dayPhase(hudClock.value));
const weather = computed(() => mapWeather(hudClock.value));
const selfCity = computed(() => props.world.cities.find((item) => item.owner === "self") ?? null);
const incoming = computed(() => {
  const home = selfCity.value;
  if (!home) {
    return [];
  }
  return incomingOnCity(props.world.marches, home.id);
});
const liveCount = computed(
  () => (props.world.marches?.length ?? 0) + (props.world.transports?.length ?? 0)
);

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
  return Math.max(16, Math.min(36, scale.value * 2.5));
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

function hash2(x: number, y: number): number {
  let n = Math.imul(x | 0, 374761393) + Math.imul(y | 0, 668265263);
  n = Math.imul(n ^ (n >>> 13), 1274126177);
  return ((n ^ (n >>> 16)) >>> 0) / 4294967295;
}

type Biome = "water" | "mountain" | "forest" | "hills" | "plain";

function biome(x: number, y: number): Biome {
  const e = hash2(x + 99, y + 17);
  const h = hash2(x, y);
  if (e < 0.055) {
    return "water";
  }
  if (h < 0.1) {
    return "mountain";
  }
  if (h < 0.26) {
    return "forest";
  }
  if (h < 0.38) {
    return "hills";
  }
  return "plain";
}

function biomeColor(kind: Biome, night: boolean): string {
  const shade = night ? 0.72 : 1;
  const colors: Record<Biome, [number, number, number]> = {
    water: [42, 78, 92],
    mountain: [86, 78, 64],
    forest: [38, 68, 36],
    hills: [78, 86, 46],
    plain: [62, 84, 42]
  };
  const [r, g, b] = colors[kind];
  return `rgb(${Math.round(r * shade)},${Math.round(g * shade)},${Math.round(b * shade)})`;
}

function mapWeather(now: number): "clear" | "mist" | "rain" {
  const day = Math.floor(now / 86400000);
  const h = hash2(day, 11);
  if (h < 0.16) {
    return "rain";
  }
  if (h < 0.34) {
    return "mist";
  }
  return "clear";
}

function weatherLabel(kind: "clear" | "mist" | "rain"): string {
  if (kind === "rain") {
    return "细雨";
  }
  if (kind === "mist") {
    return "薄雾";
  }
  return "晴朗";
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
  const now = serverNow();
  const cell = scale.value;
  const night = phase.value === "night";
  const toScreen = (x: number, y: number) => ({
    sx: w / 2 + (x - originX.value) * cell,
    sy: h / 2 + (y - originY.value) * cell
  });

  const sky = ctx.createLinearGradient(0, 0, 0, h);
  if (phase.value === "dawn") {
    sky.addColorStop(0, "#6a4a38");
    sky.addColorStop(0.5, "#2a3824");
    sky.addColorStop(1, "#1a2214");
  } else if (phase.value === "dusk") {
    sky.addColorStop(0, "#5a2a28");
    sky.addColorStop(0.5, "#2a2418");
    sky.addColorStop(1, "#14120e");
  } else if (night) {
    sky.addColorStop(0, "#121820");
    sky.addColorStop(0.55, "#10140e");
    sky.addColorStop(1, "#0a0c08");
  } else {
    sky.addColorStop(0, "#3a4c2e");
    sky.addColorStop(0.55, "#24301c");
    sky.addColorStop(1, "#16180e");
  }
  ctx.fillStyle = sky;
  ctx.fillRect(0, 0, w, h);

  const minX = Math.floor(originX.value - w / 2 / cell) - 1;
  const maxX = Math.ceil(originX.value + w / 2 / cell) + 1;
  const minY = Math.floor(originY.value - h / 2 / cell) - 1;
  const maxY = Math.ceil(originY.value + h / 2 / cell) + 1;
  const step = cell >= 14 ? 1 : cell >= 8 ? 1 : 2;

  for (let y = minY; y <= maxY; y += step) {
    for (let x = minX; x <= maxX; x += step) {
      if (x < 0 || y < 0 || x >= props.world.width || y >= props.world.height) {
        continue;
      }
      const p = toScreen(x, y);
      ctx.fillStyle = biomeColor(biome(x, y), night);
      ctx.fillRect(p.sx, p.sy, cell * step + 1, cell * step + 1);
    }
  }

  ctx.strokeStyle = night ? "rgba(40, 56, 40, 0.28)" : "rgba(70, 90, 50, 0.28)";
  ctx.lineWidth = 1;
  if (cell >= 12) {
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
    ctx.lineWidth = 2.2;
    ctx.setLineDash([7, 6]);
    ctx.lineDashOffset = -((now / 28) % 26);
    ctx.beginPath();
    ctx.moveTo(from.sx, from.sy);
    ctx.lineTo(to.sx, to.sy);
    ctx.stroke();
    ctx.restore();
    for (let i = 1; i <= 4; i++) {
      const back = pointOnPath(Math.max(0, t - i * 0.018), fromX, fromY, toX, toY, roundTrip);
      const dust = toScreen(back.x, back.y);
      ctx.fillStyle = `rgba(210, 190, 140, ${0.16 - i * 0.03})`;
      ctx.beginPath();
      ctx.arc(dust.sx, dust.sy, 3 + i, 0, Math.PI * 2);
      ctx.fill();
    }
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
  const pulse = 0.55 + Math.sin(now / 260) * 0.45;

  for (const item of props.world.outposts) {
    const p = toScreen(item.x, item.y);
    const roaming = item.kind === "roaming";
    if (roaming) {
      ctx.strokeStyle = `rgba(220, 90, 60, ${0.25 + pulse * 0.45})`;
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.arc(p.sx, p.sy, size / 2 + 6 + pulse * 5, 0, Math.PI * 2);
      ctx.stroke();
    }
    ctx.globalAlpha = item.garrison > 0 ? 1 : 0.55;
    drawSprite(ctx, roaming ? sprites.roaming : sprites.outpost, p.sx, p.sy, size);
    ctx.globalAlpha = 1;
  }

  for (const item of props.world.markets ?? []) {
    const p = toScreen(item.x, item.y);
    ctx.strokeStyle = `rgba(120, 200, 170, ${0.2 + pulse * 0.25})`;
    ctx.beginPath();
    ctx.arc(p.sx, p.sy, size / 2 + 4, 0, Math.PI * 2);
    ctx.stroke();
    drawSprite(ctx, sprites.market, p.sx, p.sy, size);
  }

  for (const item of props.world.cities) {
    const p = toScreen(item.x, item.y);
    const citySize = item.owner === "self" ? size + 6 : size;
    if (item.owner === "self") {
      ctx.strokeStyle = `rgba(212, 160, 23, ${0.25 + pulse * 0.4})`;
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.arc(p.sx, p.sy, citySize / 2 + 10 + pulse * 6, 0, Math.PI * 2);
      ctx.stroke();
    }
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

  if (cell >= 11) {
    ctx.font = "12px 'Songti SC', 'STSong', serif";
    ctx.textAlign = "center";
    ctx.textBaseline = "top";
    const drawLabel = (text: string, sx: number, sy: number, color: string) => {
      ctx.fillStyle = "rgba(12, 10, 8, 0.62)";
      const width = Math.min(160, text.length * 12 + 12);
      ctx.fillRect(sx - width / 2, sy + size / 2 + 2, width, 16);
      ctx.fillStyle = color;
      ctx.fillText(text, sx, sy + size / 2 + 3);
    };
    for (const item of props.world.cities) {
      const p = toScreen(item.x, item.y);
      drawLabel(item.name, p.sx, p.sy, item.owner === "self" ? "#e8c97a" : "#efe6d4");
    }
    for (const item of props.world.outposts) {
      const p = toScreen(item.x, item.y);
      const left =
        item.kind === "roaming" && item.expiresAt
          ? Date.parse(item.expiresAt) - now <= 0
            ? " ·即将消失"
            : ` ·${remainText(item.expiresAt, now)}`
          : "";
      drawLabel(`${item.name}${left}`, p.sx, p.sy, item.kind === "roaming" ? "#e08a7a" : "#d8c8a4");
    }
    for (const item of props.world.markets ?? []) {
      const p = toScreen(item.x, item.y);
      drawLabel(item.name, p.sx, p.sy, "#8ee0c4");
    }
  }

  for (let i = 0; i < 7; i++) {
    const drift = ((now / 40 + i * 90) % (w + 160)) - 80;
    const cy = 28 + ((i * 53) % (h * 0.45));
    ctx.fillStyle = night ? "rgba(180, 200, 220, 0.08)" : "rgba(230, 230, 220, 0.12)";
    ctx.beginPath();
    ctx.ellipse(drift, cy, 46 + (i % 3) * 12, 14, 0, 0, Math.PI * 2);
    ctx.fill();
  }

  if (weather.value === "mist") {
    ctx.fillStyle = "rgba(200, 210, 190, 0.12)";
    ctx.fillRect(0, 0, w, h);
  }
  if (weather.value === "rain") {
    ctx.strokeStyle = "rgba(190, 210, 220, 0.28)";
    ctx.lineWidth = 1;
    for (let i = 0; i < 70; i++) {
      const rx = ((now / 3 + i * 37) % (w + 20)) - 10;
      const ry = ((now / 2 + i * 53) % (h + 20)) - 10;
      ctx.beginPath();
      ctx.moveTo(rx, ry);
      ctx.lineTo(rx + 4, ry + 12);
      ctx.stroke();
    }
  }

  if (night) {
    ctx.fillStyle = "rgba(8, 12, 20, 0.18)";
    ctx.fillRect(0, 0, w, h);
  }

  const vignette = ctx.createRadialGradient(w / 2, h / 2, h * 0.35, w / 2, h / 2, h * 0.78);
  vignette.addColorStop(0, "rgba(0,0,0,0)");
  vignette.addColorStop(1, "rgba(0,0,0,0.35)");
  ctx.fillStyle = vignette;
  ctx.fillRect(0, 0, w, h);

  updateHover();
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

function nearest(
  worldX: number,
  worldY: number
): { targetType: "outpost" | "city" | "market"; targetId: number; label: string; d: number } | null {
  const radius = hitRadius();
  let best: { targetType: "outpost" | "city" | "market"; targetId: number; label: string; d: number } | null = null;
  const consider = (
    targetType: "outpost" | "city" | "market",
    targetId: number,
    label: string,
    px: number,
    py: number
  ) => {
    const d = Math.hypot(px - worldX, py - worldY);
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
      const left = Date.parse(item.expiresAt) - serverNow();
      label =
        left <= 0
          ? `${item.name}（即将消失）`
          : `${item.name}（${remainText(item.expiresAt, serverNow())}后消失）`;
    }
    if (item.garrison <= 0) {
      label = `${label}（已打空）`;
    }
    consider("outpost", item.id, label, item.x, item.y);
  }
  for (const item of props.world.markets ?? []) {
    consider("market", item.id, item.name, item.x, item.y);
  }
  return best;
}

function updateHover(): void {
  const el = canvas.value;
  if (!el || pointerMx < 0) {
    hover.value = "";
    return;
  }
  const x = originX.value + (pointerMx - el.width / 2) / scale.value;
  const y = originY.value + (pointerMy - el.height / 2) / scale.value;
  const found = nearest(x, y);
  const next = found ? found.label : `${Math.round(x)}, ${Math.round(y)}`;
  if (hover.value !== next) {
    hover.value = next;
  }
}

function hit(clientX: number, clientY: number): void {
  const el = canvas.value;
  const pt = canvasPoint(clientX, clientY);
  if (!el || !pt) {
    return;
  }
  const x = originX.value + (pt.mx - el.width / 2) / scale.value;
  const y = originY.value + (pt.my - el.height / 2) / scale.value;
  const found = nearest(x, y);
  if (found) {
    emit("select", { targetType: found.targetType, targetId: found.targetId, label: found.label });
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
  const pt = canvasPoint(ev.clientX, ev.clientY);
  if (pt) {
    pointerMx = pt.mx;
    pointerMy = pt.my;
  }
  if (!dragging) {
    return;
  }
  originX.value -= (ev.clientX - lastX) / scale.value;
  originY.value -= (ev.clientY - lastY) / scale.value;
  lastX = ev.clientX;
  lastY = ev.clientY;
}

function onPointerUp(ev: PointerEvent): void {
  if (Math.hypot(ev.clientX - startX, ev.clientY - startY) < 6) {
    hit(ev.clientX, ev.clientY);
  }
  dragging = false;
}

function onPointerLeave(): void {
  pointerMx = -1;
  pointerMy = -1;
  hover.value = "";
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
}

function recenter(): void {
  originX.value = props.world.origin.x;
  originY.value = props.world.origin.y;
  scale.value = 10;
}

function stopLoop(): void {
  if (raf) {
    cancelAnimationFrame(raf);
    raf = 0;
  }
}

function loop(): void {
  const t = Date.now();
  if (t - lastHud >= 1000) {
    lastHud = t;
    hudClock.value = t;
  }
  draw();
  if (props.active) {
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
    if (props.active) {
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
      @pointerleave="onPointerLeave"
      @wheel.prevent="onWheel"
    ></canvas>
    <div class="map-hud">
      <span>{{ dayPhaseLabel(phase) }} · {{ weatherLabel(weather) }}</span>
      <span>行军/运输 {{ liveCount }}</span>
      <span v-if="incoming.length" class="incoming">敌袭 {{ incoming.length }} 路</span>
    </div>
    <p v-if="hover" class="map-tip">{{ hover }}</p>
    <button type="button" class="map-home" @click="recenter">回城</button>
  </div>
</template>
