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
  WorldDto,
  MailListDto,
  RankingDto,
  RankingType,
  AllianceDetailDto,
  AlliancePendingDto,
  AllianceSummaryDto
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

export function fetchMail(page = 1, unreadOnly = false): Promise<MailListDto> {
  return request<MailListDto>(
    "get",
    `/api/mail?page=${page}&pageSize=20&unreadOnly=${unreadOnly ? "true" : "false"}`
  );
}

export function readMail(id: number): Promise<unknown> {
  return request("post", `/api/mail/${id}/read`);
}

export function readAllMail(): Promise<unknown> {
  return request("post", "/api/mail/read-all");
}

export function fetchRankings(type: RankingType): Promise<RankingDto> {
  return request<RankingDto>("get", `/api/rankings?type=${type}`);
}

export function fetchAlliances(page = 1): Promise<PagedResult<AllianceSummaryDto>> {
  return request<PagedResult<AllianceSummaryDto>>("get", `/api/alliances?page=${page}&pageSize=20`);
}

export function fetchMyAlliance(): Promise<AllianceDetailDto> {
  return request<AllianceDetailDto>("get", "/api/alliances/me");
}

export function fetchAlliancePending(): Promise<AlliancePendingDto> {
  return request<AlliancePendingDto>("get", "/api/alliances/pending");
}

export function createAlliance(name: string): Promise<AllianceDetailDto> {
  return request<AllianceDetailDto>("post", "/api/alliances", { name });
}

export function applyAlliance(id: number): Promise<unknown> {
  return request("post", `/api/alliances/${id}/apply`);
}

export function inviteAlliance(characterName: string): Promise<unknown> {
  return request("post", "/api/alliances/invite", { characterName });
}

export function acceptAllianceInvite(id: number): Promise<unknown> {
  return request("post", `/api/alliances/invites/${id}/accept`);
}

export function declineAllianceInvite(id: number): Promise<unknown> {
  return request("post", `/api/alliances/invites/${id}/decline`);
}

export function acceptAllianceApplication(id: number): Promise<unknown> {
  return request("post", `/api/alliances/applications/${id}/accept`);
}

export function rejectAllianceApplication(id: number): Promise<unknown> {
  return request("post", `/api/alliances/applications/${id}/reject`);
}

export function leaveAlliance(): Promise<unknown> {
  return request("post", "/api/alliances/leave");
}

export function dissolveAlliance(): Promise<unknown> {
  return request("post", "/api/alliances/dissolve");
}

export function kickAllianceMember(characterId: number): Promise<unknown> {
  return request("post", "/api/alliances/kick", { characterId });
}
