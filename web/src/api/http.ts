import axios, { type AxiosError } from "axios";
import { ApiError, type ApiEnvelope } from "./types";
import { clearTokens, getAccessToken } from "../session";

export const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE ?? "",
  timeout: 15000
});

http.interceptors.request.use((config) => {
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

    const axiosError = error as AxiosError<ApiEnvelope<unknown>>;
    const status = axiosError.response?.status;
    const envelope = axiosError.response?.data;
    if (status === 401) {
      clearTokens();
      throw new ApiError(40100, envelope?.message || "未登录或令牌无效");
    }

    throw new ApiError(
      envelope?.code ?? 50000,
      envelope?.message || axiosError.message || "网络错误"
    );
  }
}
