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
