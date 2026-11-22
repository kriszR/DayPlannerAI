import { apiPost } from "./http";

export interface AgentChatRequest {
  message: string;
  tripId: string;
  serviceDate: string;
}

export interface AgentChatResponse {
  answer: string;
}

export async function askAgentAboutTrip(
  message: string,
  tripId: string,
  serviceDate: string,
): Promise<AgentChatResponse> {
  const request: AgentChatRequest = {
    message,
    tripId,
    serviceDate,
  };

  return apiPost<AgentChatRequest, AgentChatResponse>(
    "/api/agent/chat",
    request,
  );
}
