import axios, { type AxiosError, type InternalAxiosRequestConfig } from "axios";
import { ApiError, type ApiEnvelope, type TokenResponse } from "./types";
import {
  clearTokens,
  getAccessToken,
  getRefreshToken,
  isAccessTokenExpiringSoon,
  notifyUnauthorized,
  saveTokens
} from "../session";

const baseURL = import.meta.env.VITE_API_BASE ?? "";

export const http = axios.create({
  baseURL,
  timeout: 15000
});

const raw = axios.create({
  baseURL,
  timeout: 15000
});

type RetryConfig = InternalAxiosRequestConfig & { _retry?: boolean };

let refreshing: Promise<boolean> | null = null;

http.interceptors.request.use(async (config) => {
  if (isAccessTokenExpiringSoon() && !isAuthUrl(config.url)) {
    await tryRefresh();
  }
  const token = getAccessToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

export async function request<T>(
  method: "get" | "post",
  url: string,
  body?: unknown
): Promise<T> {
  const result = await requestEnvelope<T>(method, url, body);
  return result.data as T;
}

export async function requestEnvelope<T>(
  method: "get" | "post",
  url: string,
  body?: unknown
): Promise<{ data: T; message: string }> {
  try {
    const response = await http.request<ApiEnvelope<T>>({
      method,
      url,
      data: body
    });
    const envelope = response.data;
    if (envelope.code !== 0) {
      throw new ApiError(envelope.code, envelope.message || "请求失败");
    }
    return { data: envelope.data as T, message: envelope.message || "ok" };
  } catch (error) {
    if (error instanceof ApiError) {
      throw error;
    }

    const axiosError = error as AxiosError<ApiEnvelope<unknown>, RetryConfig>;
    const status = axiosError.response?.status;
    const envelope = axiosError.response?.data;
    const config = axiosError.config as RetryConfig | undefined;

    if (status === 401 && config && !config._retry && !isAuthUrl(config.url)) {
      const ok = await tryRefresh();
      if (ok) {
        config._retry = true;
        const token = getAccessToken();
        if (token) {
          config.headers = config.headers ?? {};
          config.headers.Authorization = `Bearer ${token}`;
        }
        const retry = await http.request<ApiEnvelope<T>>(config);
        if (retry.data.code !== 0) {
          throw new ApiError(retry.data.code, retry.data.message || "请求失败");
        }
        return { data: retry.data.data as T, message: retry.data.message || "ok" };
      }
      notifyUnauthorized();
      throw new ApiError(40100, envelope?.message || "未登录或令牌无效");
    }

    if (status === 401) {
      if (!isAuthUrl(config?.url)) {
        notifyUnauthorized();
      } else {
        clearTokens();
      }
      throw new ApiError(40100, envelope?.message || "未登录或令牌无效");
    }

    throw new ApiError(
      envelope?.code ?? 50000,
      envelope?.message || axiosError.message || "网络错误"
    );
  }
}

function isAuthUrl(url?: string): boolean {
  if (!url) {
    return false;
  }
  return /\/api\/auth\/(login|register|refresh|logout)(?:\?|$)/.test(url);
}

async function tryRefresh(): Promise<boolean> {
  if (!refreshing) {
    refreshing = (async () => {
      const refreshToken = getRefreshToken();
      if (!refreshToken) {
        return false;
      }
      try {
        const response = await raw.post<ApiEnvelope<TokenResponse>>("/api/auth/refresh", { refreshToken });
        const envelope = response.data;
        if (envelope.code !== 0 || !envelope.data) {
          return false;
        }
        saveTokens(envelope.data.accessToken, envelope.data.refreshToken, envelope.data.expiresAt);
        return true;
      } catch {
        return false;
      }
    })().finally(() => {
      refreshing = null;
    });
  }
  return refreshing;
}
