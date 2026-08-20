import { request, requestEnvelope } from "./http";
import type {
  ArmyOverviewDto,
  BuildingsOverviewDto,
  CharacterResponse,
  CityResponse,
  FieldsCollectDto,
  FieldsOverviewDto,
  PagedResult,
  BattleReportDto,
  SessionResponse,
  TokenResponse,
  WallsOverviewDto,
  WorldDto
} from "./types";

export function register(username: string, password: string): Promise<TokenResponse> {
  return request<TokenResponse>("post", "/api/auth/register", { username, password });
}

export function login(username: string, password: string): Promise<TokenResponse> {
  return request<TokenResponse>("post", "/api/auth/login", { username, password });
}

export function logout(refreshToken: string): Promise<unknown> {
  return request("post", "/api/auth/logout", { refreshToken });
}

export function fetchSession(): Promise<SessionResponse> {
  return request<SessionResponse>("get", "/api/auth/me");
}

export function createCharacter(name: string): Promise<CharacterResponse> {
  return request<CharacterResponse>("post", "/api/characters", { name });
}

export function foundCity(): Promise<CityResponse> {
  return request<CityResponse>("post", "/api/city/found");
}

export function fetchBuildings(): Promise<BuildingsOverviewDto> {
  return request<BuildingsOverviewDto>("get", "/api/buildings");
}

export function upgradeBuilding(buildingType: string): Promise<BuildingsOverviewDto> {
  return request<BuildingsOverviewDto>("post", "/api/buildings/upgrade", { buildingType });
}

export function fetchFields(): Promise<FieldsOverviewDto> {
  return request<FieldsOverviewDto>("get", "/api/fields");
}

export function upgradeField(fieldType: string): Promise<FieldsOverviewDto> {
  return request<FieldsOverviewDto>("post", "/api/fields/upgrade", { fieldType });
}

export function collectFields(fieldType?: string): Promise<{ data: FieldsCollectDto; message: string }> {
  return requestEnvelope<FieldsCollectDto>("post", "/api/fields/collect", fieldType ? { fieldType } : {});
}

export function fetchWalls(): Promise<WallsOverviewDto> {
  return request<WallsOverviewDto>("get", "/api/walls");
}

export function upgradeWall(wallType: string): Promise<WallsOverviewDto> {
  return request<WallsOverviewDto>("post", "/api/walls/upgrade", { wallType });
}

export function fetchArmy(): Promise<ArmyOverviewDto> {
  return request<ArmyOverviewDto>("get", "/api/army");
}

export function recruit(troopType: string, count: number): Promise<ArmyOverviewDto> {
  return request<ArmyOverviewDto>("post", "/api/army/recruit", { troopType, count });
}

export function march(
  targetType: string,
  targetId: number,
  infantry: number,
  archer: number,
  cavalry: number
): Promise<ArmyOverviewDto> {
  return request<ArmyOverviewDto>("post", "/api/army/march", { targetType, targetId, infantry, archer, cavalry });
}

export function fetchReports(page = 1): Promise<PagedResult<BattleReportDto>> {
  return request<PagedResult<BattleReportDto>>("get", `/api/reports?page=${page}&pageSize=20`);
}

export function fetchWorld(): Promise<WorldDto> {
  return request<WorldDto>("get", "/api/world");
}
