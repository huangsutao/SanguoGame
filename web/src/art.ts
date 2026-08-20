export const resourceArt: Record<string, string> = {
  grain: "/art/grain.jpg",
  wood: "/art/wood.jpg",
  iron: "/art/iron.jpg",
  copper: "/art/copper.jpg"
};

export const buildingArt: Record<string, string> = {
  palace: "/art/palace.jpg",
  house: "/art/house.jpg",
  warehouse: "/art/warehouse.jpg",
  academy: "/art/academy.jpg",
  barracks: "/art/barracks.jpg",
  drillHall: "/art/drill-hall.jpg",
  defenseHall: "/art/defense-hall.jpg",
  resourceHall: "/art/resource-hall.jpg",
  farm: "/art/farm.jpg",
  lumber: "/art/lumber.jpg",
  ironMine: "/art/iron-mine.jpg",
  copperMine: "/art/copper-mine.jpg",
  arrowTower: "/art/wall.jpg",
  gate: "/art/gate.jpg",
  trap: "/art/trap.jpg"
};

export const troopArt: Record<string, string> = {
  infantry: "/art/infantry.jpg",
  archer: "/art/archer.jpg",
  cavalry: "/art/cavalry.jpg"
};

export const markerArt = {
  city: "/art/marker-city.jpg",
  outpost: "/art/marker-outpost.jpg",
  roaming: "/art/marker-roaming.jpg",
  market: "/art/marker-market.jpg"
};

export const resourceKeys = ["grain", "wood", "iron", "copper"] as const;

export function buildingPortrait(type: string): string {
  return buildingArt[type] ?? "/art/palace.jpg";
}

export function troopPortrait(type: string): string {
  return troopArt[type] ?? "/art/infantry.jpg";
}
