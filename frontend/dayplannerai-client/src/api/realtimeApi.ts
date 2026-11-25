import { apiGet } from "./http";

export interface RealtimeVehicle {
    vehicleId: string;
    tripId: string;
    latitude: number;
    longitude: number;
    heading: number | null;
    speedMetersPerSecond: number | null;
    speedKilometersPerHour: number | null;
    lastUpdatedUtc: string;
    isFresh: boolean;
}

export interface RealtimeStopCall {
    stopId: string;
    stopName: string;
    arrivalDelaySeconds: number | null;
    departureDelaySeconds: number | null;
    realtime: boolean;
    realtimeState: string | null;
}

export interface RealtimeTrip {
    tripId: string;
    serviceDate: string;
    routeId: string;
    routeShortName: string | null;
    routeLongName: string | null;
    tripHeadsign: string | null;
    hasRealtimeStopData: boolean;
    retrievedAtUtc: string;
    vehiclePosition: RealtimeVehicle | null;
    stopCalls: RealtimeStopCall[];
}

export async function getTripRealtime(
    tripId: string,
    serviceDate: string
): Promise<RealtimeTrip> {
    return apiGet<RealtimeTrip>(
        `/realtime/trips/${encodeURIComponent(tripId)}?serviceDate=${encodeURIComponent(serviceDate)}`
    );
}