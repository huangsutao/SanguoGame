export interface ApiEnvelope<T> {
  code: number;
  message: string;
  data?: T;
  traceId?: string;
}

export class ApiError extends Error {
  readonly code: number;

  constructor(code: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.code = code;
  }
}

export interface TokenResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  tokenType: string;
}

export interface SessionCharacter {
  id: number;
  name: string;
}

export interface SessionCity {
  id: number;
  name: string;
  x: number;
  y: number;
}

export interface SessionResponse {
  accountId: number;
  username: string;
  character?: SessionCharacter;
  city?: SessionCity;
}

export interface CharacterResponse {
  id: number;
  name: string;
  createdAt: string;
}

export interface CityResponse {
  id: number;
  characterId: number;
  name: string;
  x: number;
  y: number;
  createdAt: string;
}

export interface ResourceDto {
  grain: number;
  wood: number;
  iron: number;
  copper: number;
}

export interface BuildingCostDto {
  level: number;
  durationSeconds: number;
  cost: ResourceDto;
}

export interface BuildingQueueDto {
  buildingType: string;
  targetLevel: number;
  finishAt: string;
}

export interface BuildingItemDto {
  type: string;
  name: string;
  category: string;
  level: number;
  maxLevel: number;
  status: "idle" | "upgrading";
  targetLevel?: number;
  finishAt?: string;
  effects: Record<string, number>;
  next?: BuildingCostDto;
  blockedReason?: string;
}

export interface BuildingsOverviewDto {
  cityId: number;
  serverTime: string;
  resources: ResourceDto;
  resourceCap: number;
  populationCap: number;
  queue?: BuildingQueueDto;
  buildings: BuildingItemDto[];
}

export interface FieldItemDto {
  type: string;
  name: string;
  resource: string;
  level: number;
  maxLevel: number;
  status: "idle" | "upgrading";
  targetLevel?: number;
  finishAt?: string;
  ratePerHour: number;
  fieldCap: number;
  pending: number;
  lastCollectedAt?: string;
  next?: BuildingCostDto;
  blockedReason?: string;
}

export interface FieldsOverviewDto {
  cityId: number;
  serverTime: string;
  resources: ResourceDto;
  resourceCap: number;
  queue?: BuildingQueueDto;
  fields: FieldItemDto[];
}

export interface FieldsCollectDto {
  cityId: number;
  serverTime: string;
  resources: ResourceDto;
  resourceCap: number;
  collected: ResourceDto;
  fields: FieldItemDto[];
}
