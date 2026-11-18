
import { apiGet, apiPut } from "./http";

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

export interface UpdateProfileRequest {
    displayName: string;
    homeLocation: string | null;
    timeZone: string;
}

export interface UpdateProfileResponse {
    message: string;
    profile: UserProfile;
}

export function getProfile(): Promise<UserProfile> {
    return apiGet<UserProfile>("/profile");
}

export function checkApiHealth(): Promise<ApiHealth> {
    return apiGet<ApiHealth>("/health");
}

export function updateProfile(
    data: UpdateProfileRequest
): Promise<UpdateProfileResponse> {
    return apiPut<UpdateProfileRequest, UpdateProfileResponse>(
        "/profile",
        data
    );
}
