import { apiPost } from "./http";
import type { GtfsDirectRoute, GtfsJourney, TravelPlan } from "./aiApi";

export interface BusSearchRequest {
  departureLocation: string;
  destination: string;
  travelDate: string;
  searchMode: "departAfter" | "arriveBy";
  departureTime?: string;
  arrivalTime?: string;
}

export interface BusSearchResponse {
  success: boolean;
  travelPlan: TravelPlan;
  availableRoutes: GtfsDirectRoute[];
  availableJourneys: GtfsJourney[];
  resultCount: number;
  directRoutesFound: boolean;
  transferSearchPerformed: boolean;
}

export function searchBusJourneys(
  request: BusSearchRequest,
): Promise<BusSearchResponse> {
  return apiPost<BusSearchRequest, BusSearchResponse>(
    "/travel/search",
    request,
  );
}
