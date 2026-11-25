import { apiPost, apiDelete } from "./http";

export type AiChatRole = "user" | "assistant";

export interface AiChatMessage {
  role: AiChatRole;
  content: string;
}

export interface TravelPlan {
  departureLocation: string | null;
  destination: string | null;
  travelDate: string | null;
  arrivalTime: string | null;
  transportMode: "bus" | "train" | "car" | "walking" | "unknown";
}

export interface AiChatRequest {
  conversationId: string;
  messages: AiChatMessage[];
}

export interface GtfsDirectRoute {
  tripId: string;
  routeId: string;
  routeShortName: string | null;
  routeLongName: string | null;
  tripHeadsign: string | null;
  departureStop: string;
  departurePlatform: string | null;
  departureTime: string;
  arrivalStop: string;
  arrivalPlatform: string | null;
  arrivalTime: string;
  durationMinutes: number;
  serviceDate: string;
  isInProgress: boolean;
}

export interface GtfsJourneyLeg {
  tripId: string;
  routeId: string;
  routeShortName: string | null;
  routeLongName: string | null;
  tripHeadsign: string | null;
  departureStop: string;
  departurePlatform: string | null;
  departureTime: string;
  arrivalStop: string;
  arrivalPlatform: string | null;
  arrivalTime: string;
  durationMinutes: number;
  isInProgress: boolean;
}

export interface GtfsJourney {
  departureStop: string;
  arrivalStop: string;
  departureTime: string;
  arrivalTime: string;
  totalDurationMinutes: number;
  transferCount: number;
  serviceDate: string;
  legs: GtfsJourneyLeg[];
}

export interface AiChatResponse {
  answer: string;
  conversationId: string;
  travelPlan: TravelPlan | null;
  availableRoutes: GtfsDirectRoute[] | null;
  availableJourneys: GtfsJourney[] | null;
}

export async function sendAiMessage(
  conversationId: string,
  messages: AiChatMessage[],
): Promise<AiChatResponse> {
  return apiPost<AiChatRequest, AiChatResponse>("/ai/chat", {
    conversationId,
    messages,
  });
}

export async function clearAiConversation(
  conversationId: string,
): Promise<void> {
  await apiDelete(`/ai/conversation/${encodeURIComponent(conversationId)}`);
}
