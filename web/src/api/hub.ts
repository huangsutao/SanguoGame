import * as signalR from "@microsoft/signalr";
import { getAccessToken } from "../session";

export function createGameHub(): signalR.HubConnection {
  const base = import.meta.env.VITE_API_BASE ?? "";
  return new signalR.HubConnectionBuilder()
    .withUrl(`${base}/hubs/game`, { accessTokenFactory: () => getAccessToken() ?? "" })
    .withAutomaticReconnect()
    .build();
}
