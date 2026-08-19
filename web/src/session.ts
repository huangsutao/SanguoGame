const accessKey = "token";
const refreshKey = "refreshToken";

export function getAccessToken(): string | null {
  return localStorage.getItem(accessKey);
}

export function getRefreshToken(): string | null {
  return localStorage.getItem(refreshKey);
}

export function saveTokens(accessToken: string, refreshToken: string): void {
  localStorage.setItem(accessKey, accessToken);
  localStorage.setItem(refreshKey, refreshToken);
}

export function clearTokens(): void {
  localStorage.removeItem(accessKey);
  localStorage.removeItem(refreshKey);
}
