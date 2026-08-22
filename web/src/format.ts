import type { BuildingCostDto, FieldItemDto } from "./api/types";
import { resourceKeys } from "./art";

export const resourceLabel: Record<string, string> = {
  grain: "粮",
  wood: "木",
  iron: "铁",
  copper: "铜"
};

export const troopLabel: Record<string, string> = {
  infantry: "步兵",
  archer: "弓兵",
  cavalry: "骑兵"
};

export type DayPhase = "dawn" | "day" | "dusk" | "night";

export function remainText(finishAt: string | undefined, nowMs: number, overdue = "即将完成"): string {
  if (!finishAt) {
    return "";
  }
  const end = Date.parse(finishAt);
  if (!Number.isFinite(end)) {
    return "";
  }
  const ms = end - nowMs;
  if (ms <= 0) {
    return overdue;
  }
  const sec = Math.ceil(ms / 1000);
  const m = Math.floor(sec / 60);
  const s = sec % 60;
  return m > 0 ? `${m}分${s}秒` : `${s}秒`;
}

export function calibratedNow(serverTime: string | undefined, capturedAt: number, nowMs: number): number {
  const parsed = serverTime ? Date.parse(serverTime) : NaN;
  if (!Number.isFinite(parsed)) {
    return nowMs;
  }
  return parsed + (nowMs - capturedAt);
}

export function blockedText(reason?: string): string {
  switch (reason) {
    case "queue":
      return "队列占用中";
    case "maxLevel":
      return "已满级";
    case "prerequisite":
      return "前置未满足";
    case "resources":
      return "资源不足";
    default:
      return "";
  }
}

export function effectsText(effects?: Record<string, number>): string {
  if (!effects) {
    return "";
  }
  const labels: Record<string, [string, "percent" | "flat"]> = {
    populationCap: ["人口上限", "flat"],
    resourceCap: ["仓库上限", "flat"],
    attackBonusPercent: ["攻方战力", "percent"],
    troopPowerBonusPercent: ["兵力战力", "percent"],
    recruitDiscountPercent: ["征兵减免", "percent"],
    wallDefenseFlat: ["城防", "flat"],
    trapBonusPercent: ["陷阱", "percent"],
    productionBonusPercent: ["田产出", "percent"],
    troopCap: ["带兵上限", "flat"],
    wallDefense: ["城防", "flat"],
    trapBonus: ["陷阱", "percent"]
  };
  return Object.entries(effects)
    .map(([key, value]) => {
      const [name, kind] = labels[key] ?? [key, "flat"];
      return kind === "percent" ? `${name}+${value}%` : `${name}+${value}`;
    })
    .join(" · ");
}

export function costParts(next?: BuildingCostDto): { key: (typeof resourceKeys)[number]; amount: number }[] {
  if (!next) {
    return [];
  }
  return resourceKeys
    .map((key) => ({ key, amount: next.cost[key] }))
    .filter((item) => item.amount > 0);
}

export function liveFieldPending(field: FieldItemDto, nowMs: number, snapshotMs?: number): number {
  if (field.level < 1 || field.fieldCap <= 0) {
    return 0;
  }
  const base = Math.max(0, field.pending);
  if (field.ratePerHour <= 0 || snapshotMs == null || !Number.isFinite(snapshotMs)) {
    return Math.min(field.fieldCap, base);
  }
  const elapsedSec = Math.max(0, nowMs - snapshotMs) / 1000;
  const extra = Math.floor((field.ratePerHour * elapsedSec) / 3600);
  return Math.min(field.fieldCap, base + extra);
}

export function incomingOnCity(marches: { mine: boolean; status: string; targetType: string; targetId: number; kind?: string }[], cityId: number) {
  return marches.filter(
    (item) =>
      !item.mine &&
      item.status === "marching" &&
      item.targetType === "city" &&
      item.targetId === cityId &&
      item.kind !== "scout"
  );
}

export function dayPhase(nowMs = Date.now()): DayPhase {
  const date = new Date(nowMs);
  const hour = date.getHours() + date.getMinutes() / 60;
  if (hour >= 5 && hour < 7.5) {
    return "dawn";
  }
  if (hour >= 7.5 && hour < 17.2) {
    return "day";
  }
  if (hour >= 17.2 && hour < 19.2) {
    return "dusk";
  }
  return "night";
}

export function dayPhaseLabel(phase: DayPhase): string {
  switch (phase) {
    case "dawn":
      return "黎明";
    case "dusk":
      return "黄昏";
    case "night":
      return "夜色";
    default:
      return "白昼";
  }
}
