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

export interface AiChatResponse {
  answer: string;
  conversationId: string;
  travelPlan: TravelPlan | null;
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
