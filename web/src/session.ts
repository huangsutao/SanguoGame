const accessKey = "sanguo.accessToken";
const refreshKey = "sanguo.refreshToken";
const expiresKey = "sanguo.accessExpiresAt";

export function getAccessToken(): string | null {
  return localStorage.getItem(accessKey) ?? localStorage.getItem("token");
}

export function getRefreshToken(): string | null {
  return localStorage.getItem(refreshKey) ?? localStorage.getItem("refreshToken");
}

export function getAccessExpiresAt(): number | null {
  const raw = localStorage.getItem(expiresKey);
  if (!raw) {
    return null;
  }
  const value = Date.parse(raw);
  return Number.isFinite(value) ? value : null;
}

export function saveTokens(accessToken: string, refreshToken: string, expiresAt?: string): void {
  localStorage.setItem(accessKey, accessToken);
  localStorage.setItem(refreshKey, refreshToken);
  if (expiresAt) {
    localStorage.setItem(expiresKey, expiresAt);
  }
  localStorage.removeItem("token");
  localStorage.removeItem("refreshToken");
}

export function clearTokens(): void {
  localStorage.removeItem(accessKey);
  localStorage.removeItem(refreshKey);
  localStorage.removeItem(expiresKey);
  localStorage.removeItem("token");
  localStorage.removeItem("refreshToken");
}

export function isAccessTokenExpiringSoon(skewMs = 30_000): boolean {
  const expires = getAccessExpiresAt();
  if (expires === null) {
    return false;
  }
  return expires - Date.now() <= skewMs;
}

type UnauthorizedHandler = () => void;

let unauthorizedHandler: UnauthorizedHandler | null = null;

export function setUnauthorizedHandler(handler: UnauthorizedHandler | null): void {
  unauthorizedHandler = handler;
}

export function notifyUnauthorized(): void {
  clearTokens();
  unauthorizedHandler?.();
}
