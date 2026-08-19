import { request } from "./http";
import type {
  BuildingsOverviewDto,
  CharacterResponse,
  CityResponse,
  SessionResponse,
  TokenResponse
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
