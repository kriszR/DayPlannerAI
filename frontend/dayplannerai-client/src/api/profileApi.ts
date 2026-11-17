
import { apiGet } from "./http";

export interface UserProfile {
    id: number;
    displayName: string;
    homeLocation: string | null;
    timeZone: string;
    createdAt: string;
}

export interface ApiHealth {
    status: string;
    application: string;
    timestamp: string;
}

export function getProfile(): Promise<UserProfile> {
    return apiGet<UserProfile>("/profile");
}

export function checkApiHealth(): Promise<ApiHealth> {
    return apiGet<ApiHealth>("/health");
}
