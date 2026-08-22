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

export interface QueueStateDto {
  used: number;
  limit: number;
  extra: number;
}

export interface CityQueueSlotsDto {
  build: QueueStateDto;
  field: QueueStateDto;
  tech: QueueStateDto;
  recruit: QueueStateDto;
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
  queues?: BuildingQueueDto[];
  buildSlots?: QueueStateDto;
  techSlots?: QueueStateDto;
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
  queues?: BuildingQueueDto[];
  fieldSlots?: QueueStateDto;
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

export interface WallsOverviewDto {
  cityId: number;
  serverTime: string;
  resources: ResourceDto;
  resourceCap: number;
  wallDefense: number;
  trapBonus: number;
  queue?: BuildingQueueDto;
  queues?: BuildingQueueDto[];
  buildSlots?: QueueStateDto;
  walls: BuildingItemDto[];
}

export interface TroopDto {
  infantry: number;
  archer: number;
  cavalry: number;
}

export interface TroopTypeDto {
  type: string;
  name: string;
  requireBarracksLevel: number;
  unitCost: ResourceDto;
}

export interface MarchDto {
  id: number;
  targetType: "outpost" | "city";
  targetId: number;
  fromX: number;
  fromY: number;
  toX: number;
  toY: number;
  troops?: TroopDto;
  departAt: string;
  arriveAt: string;
  status: "marching" | "settled";
  mine: boolean;
  kind?: "attack" | "scout";
}

export interface ArmyOverviewDto {
  cityId: number;
  serverTime: string;
  resources: ResourceDto;
  resourceCap: number;
  troops: TroopDto;
  troopCap: number;
  barracksLevel: number;
  wallDefense: number;
  protectionUntil?: string;
  marches: MarchDto[];
  troopTypes: TroopTypeDto[];
  troopPowerBonusPercent?: number;
  recruitDiscountPercent?: number;
  recruitQueue?: RecruitQueueDto;
  recruitQueues?: RecruitQueueDto[];
  recruitSlots?: QueueStateDto;
}

export interface RecruitQueueDto {
  troopType: string;
  count: number;
  finishAt: string;
}

export interface BattleReportDto {
  id: number;
  marchId: number;
  attackerCityId: number;
  defenderType: "outpost" | "city";
  defenderId: number;
  attackerWon: boolean;
  attackerBefore: TroopDto;
  attackerAfter: TroopDto;
  defenderBefore: TroopDto;
  defenderAfter: TroopDto;
  loot: ResourceDto;
  seed: number;
  summary: string;
  createdAt: string;
  yuanbao?: number;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

export interface WorldCityDto {
  id: number;
  name: string;
  x: number;
  y: number;
  owner: "self" | "ai" | "player";
  protected: boolean;
}

export interface WorldOutpostDto {
  id: number;
  type: string;
  name: string;
  x: number;
  y: number;
  garrison: number;
  kind?: "permanent" | "roaming";
  expiresAt?: string;
}

export interface WorldDto {
  width: number;
  height: number;
  serverTime: string;
  origin: { x: number; y: number };
  cities: WorldCityDto[];
  outposts: WorldOutpostDto[];
  marches: MarchDto[];
  markets: WorldMarketDto[];
  transports: TransportDto[];
}

export interface WorldMarketDto {
  id: number;
  name: string;
  x: number;
  y: number;
}

export type TransportKind = "market" | "aid";
export type TransportStatus = "inTransit" | "settled";

export interface TransportDto {
  id: number;
  kind: TransportKind;
  fromCityId: number;
  toCityId: number;
  targetId: number;
  fromX: number;
  fromY: number;
  toX: number;
  toY: number;
  cargo: ResourceDto;
  credit: ResourceDto;
  departAt: string;
  arriveAt: string;
  status: TransportStatus;
  mine: boolean;
}

export interface MarketRateDto {
  fromResource: string;
  toResource: string;
  fromAmount: number;
  toAmount: number;
}

export interface MarketItemDto {
  id: number;
  name: string;
  x: number;
  y: number;
  durationSeconds: number;
  roundTripSeconds: number;
}

export interface MarketsOverviewDto {
  cityId: number;
  serverTime: string;
  resources: ResourceDto;
  resourceCap: number;
  cargoCap: number;
  taxRate: number;
  minAmount: number;
  values: ResourceDto;
  rates: MarketRateDto[];
  markets: MarketItemDto[];
  transports: TransportDto[];
}

export interface MarchTarget {
  targetType: "outpost" | "city" | "market";
  targetId: number;
  label: string;
}

export type MailType = "system" | "battle" | "alliance" | "scout";

export interface DailyMissionDto {
  type: string;
  name: string;
  detail: string;
  progress: number;
  required: number;
  claimed: boolean;
  reward: ResourceDto;
}

export interface DailyOverviewDto {
  serverTime: string;
  day: string;
  resources: ResourceDto;
  resourceCap: number;
  missions: DailyMissionDto[];
}

export interface MailDto {
  id: number;
  type: MailType;
  title: string;
  body: string;
  relatedType?: string;
  relatedId?: number;
  isRead: boolean;
  createdAt: string;
}

export interface MailListDto {
  unreadCount: number;
  items: MailDto[];
  page: number;
  pageSize: number;
  total: number;
}

export type AllianceRole = "leader" | "officer" | "member";

export interface AllianceMemberDto {
  characterId: number;
  name: string;
  role: AllianceRole;
  joinedAt: string;
  cityId: number;
}

export interface AllianceSummaryDto {
  id: number;
  name: string;
  memberCount: number;
  leaderName: string;
}

export interface AllianceDetailDto {
  id: number;
  name: string;
  notice: string;
  leaderCharacterId: number;
  memberCount: number;
  myRole?: AllianceRole;
  members: AllianceMemberDto[];
}

export interface AllianceInviteDto {
  id: number;
  allianceId: number;
  allianceName: string;
  inviterName: string;
  createdAt: string;
}

export interface AllianceApplicationDto {
  id: number;
  allianceId: number;
  characterId: number;
  characterName: string;
  createdAt: string;
}

export interface AlliancePendingDto {
  invites: AllianceInviteDto[];
  applications: AllianceApplicationDto[];
}

export type RankingType = "power" | "troops" | "loot";

export interface RankingEntryDto {
  rank: number;
  cityId: number;
  characterName: string;
  cityName: string;
  score: number;
  isAi: boolean;
  allianceName?: string;
}

export interface RankingDto {
  type: RankingType;
  serverTime: string;
  myRank?: number;
  myScore: number;
  items: RankingEntryDto[];
}

export type ItemKind = "buff" | "consumable" | "unlock";

export interface ShopCatalogItemDto {
  type: string;
  name: string;
  kind: ItemKind;
  price: number;
  durationHours?: number;
  speedPercent?: number;
  owned: number;
  description: string;
}

export interface ShopBuffDto {
  type: string;
  name: string;
  expireAt: string;
  speedPercent: number;
}

export interface ShopOverviewDto {
  cityId: number;
  serverTime: string;
  yuanbao: number;
  x: number;
  y: number;
  protectionUntil?: string;
  catalog: ShopCatalogItemDto[];
  buffs: ShopBuffDto[];
  slots?: CityQueueSlotsDto;
}
