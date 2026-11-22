import {
  useEffect,
  useRef,
  useState,
  type FormEvent,
  type KeyboardEvent,
} from "react";

import {
  Bot,
  Send,
  UserRound,
  Sparkles,
  Trash2,
  LoaderCircle,
  MapPin,
  CalendarDays,
  Clock,
  Bus,
} from "lucide-react";

import TravelRouteCards from "../components/TravelRouteCards";
import type { GtfsDirectRoute, GtfsJourney } from "../api/aiApi";

import {
  sendAiMessage,
  clearAiConversation,
  type AiChatMessage,
  type TravelPlan,
} from "../api/aiApi";

import "./Assistant.css";
import TravelJourneyCards from "../components/TravelJourneyCards";

type MessageRole = "user" | "assistant";

interface ChatMessage {
  id: string;
  role: MessageRole;
  content: string;
  createdAt: Date;
  availableRoutes?: GtfsDirectRoute[];
  availableJourneys?: GtfsJourney[];
}

const WELCOME_MESSAGE_ID = "welcome";

const STORAGE_KEYS = {
  conversationId: "dayplannerai_conversation_id",
  messages: "dayplannerai_messages",
  travelPlan: "dayplannerai_travel_plan",
};

function createConversationId(): string {
  return crypto.randomUUID().replaceAll("-", "");
}

function createWelcomeMessage(): ChatMessage {
  return {
    id: WELCOME_MESSAGE_ID,
    role: "assistant",
    content:
      "Szia! A DayPlannerAI személyi asszisztense vagyok. " +
      "Miben segíthetek megtervezni a napodat?",
    createdAt: new Date(),
  };
}

function loadMessages(): ChatMessage[] {
  try {
    const stored = localStorage.getItem(STORAGE_KEYS.messages);

    if (!stored) {
      return [createWelcomeMessage()];
    }

    const parsed = JSON.parse(stored) as ChatMessage[];

    if (!Array.isArray(parsed) || parsed.length === 0) {
      return [createWelcomeMessage()];
    }

    return parsed
      .filter(
        (message) =>
          (message.role === "user" || message.role === "assistant") &&
          typeof message.content === "string",
      )
      .map((message) => ({
        ...message,
        createdAt: new Date(message.createdAt),
      }));
  } catch {
    return [createWelcomeMessage()];
  }
}

function loadTravelPlan(): TravelPlan | null {
  try {
    const stored = localStorage.getItem(STORAGE_KEYS.travelPlan);

    return stored ? (JSON.parse(stored) as TravelPlan) : null;
  } catch {
    return null;
  }
}

function formatTime(date: Date): string {
  return new Intl.DateTimeFormat("hu-HU", {
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function formatTravelDate(date: string | null): string {
  if (!date) {
    return "Nincs megadva";
  }

  const parsedDate = new Date(`${date}T12:00:00`);

  if (Number.isNaN(parsedDate.getTime())) {
    return date;
  }

  return new Intl.DateTimeFormat("hu-HU", {
    year: "numeric",
    month: "long",
    day: "numeric",
  }).format(parsedDate);
}

function formatTransportMode(mode: TravelPlan["transportMode"]): string {
  const labels: Record<TravelPlan["transportMode"], string> = {
    bus: "Autóbusz",
    train: "Vonat",
    car: "Autó",
    walking: "Gyalog",
    unknown: "Nincs megadva",
  };

  return labels[mode];
}

export default function Assistant() {
  const [conversationId, setConversationId] = useState<string>(
    () =>
      localStorage.getItem(STORAGE_KEYS.conversationId) ??
      createConversationId(),
  );

  const [messages, setMessages] = useState<ChatMessage[]>(loadMessages);

  const [travelPlan, setTravelPlan] = useState<TravelPlan | null>(
    loadTravelPlan,
  );

  const [input, setInput] = useState("");
  const [isSending, setIsSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const messagesEndRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    localStorage.setItem(STORAGE_KEYS.conversationId, conversationId);
  }, [conversationId]);

  useEffect(() => {
    localStorage.setItem(STORAGE_KEYS.messages, JSON.stringify(messages));
  }, [messages]);

  useEffect(() => {
    if (travelPlan) {
      localStorage.setItem(STORAGE_KEYS.travelPlan, JSON.stringify(travelPlan));
    } else {
      localStorage.removeItem(STORAGE_KEYS.travelPlan);
    }
  }, [travelPlan]);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({
      behavior: "smooth",
    });
  }, [messages, isSending]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const message = input.trim();

    if (!message || isSending) {
      return;
    }

    const userMessage: ChatMessage = {
      id: crypto.randomUUID(),
      role: "user",
      content: message,
      createdAt: new Date(),
    };

    const nextMessages = [...messages, userMessage];

    setMessages(nextMessages);
    setInput("");
    setError(null);
    setIsSending(true);

    try {
      const apiMessages: AiChatMessage[] = nextMessages
        .filter((item) => item.id !== WELCOME_MESSAGE_ID)
        .slice(-12)
        .map((item) => ({
          role: item.role,
          content: item.content,
        }));

      const response = await sendAiMessage(conversationId, apiMessages);

      setConversationId(response.conversationId);

      const assistantMessage: ChatMessage = {
        id: crypto.randomUUID(),
        role: "assistant",
        content: response.answer,
        createdAt: new Date(),
        availableRoutes: response.availableRoutes ?? [],
        availableJourneys: response.availableJourneys ?? [],
      };

      setMessages((current) => [...current, assistantMessage]);

      if (response.travelPlan) {
        setTravelPlan(response.travelPlan);
      }
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Hiba történt az üzenet küldésekor.",
      );
    } finally {
      setIsSending(false);
      textareaRef.current?.focus();
    }
  }

  function handleKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      event.currentTarget.form?.requestSubmit();
    }
  }

  async function handleClearChat() {
    if (isSending) {
      return;
    }

    try {
      await clearAiConversation(conversationId);
    } catch {
      setError("A szerveroldali beszélgetési állapot törlése nem sikerült.");
      return;
    }

    const newConversationId = createConversationId();

    setConversationId(newConversationId);
    setMessages([createWelcomeMessage()]);
    setTravelPlan(null);
    setInput("");
    setError(null);
  }

  const hasTravelInformation = Boolean(
    travelPlan &&
    (travelPlan.departureLocation ||
      travelPlan.destination ||
      travelPlan.travelDate ||
      travelPlan.arrivalTime),
  );

  return (
    <div className="assistant-page">
      <div className="assistant-container">
        <header className="assistant-header">
          <div className="assistant-heading">
            <div className="assistant-heading-icon">
              <Bot size={26} />
            </div>

            <div>
              <h1>AI asszisztens</h1>
              <p>A személyi asszisztensed, mindig készen áll.</p>
            </div>
          </div>

          <button
            type="button"
            className="assistant-clear-button"
            onClick={handleClearChat}
            disabled={isSending}
            title="Új beszélgetés"
          >
            <Trash2 size={17} />
            <span>Új beszélgetés</span>
          </button>
        </header>

        {hasTravelInformation && travelPlan && (
          <section className="travel-plan-card">
            <div className="travel-plan-title">
              <Bus size={18} />
              <strong>Utazási terv</strong>
            </div>

            <div className="travel-plan-grid">
              <div className="travel-plan-item">
                <MapPin size={16} />
                <div>
                  <span>Indulás</span>
                  <strong>
                    {travelPlan.departureLocation ?? "Nincs megadva"}
                  </strong>
                </div>
              </div>

              <div className="travel-plan-item">
                <MapPin size={16} />
                <div>
                  <span>Úti cél</span>
                  <strong>{travelPlan.destination ?? "Nincs megadva"}</strong>
                </div>
              </div>

              <div className="travel-plan-item">
                <CalendarDays size={16} />
                <div>
                  <span>Utazás napja</span>
                  <strong>{formatTravelDate(travelPlan.travelDate)}</strong>
                </div>
              </div>

              <div className="travel-plan-item">
                <Clock size={16} />
                <div>
                  <span>Érkezés</span>
                  <strong>
                    {travelPlan.arrivalTime
                      ? travelPlan.arrivalTime.slice(0, 5)
                      : "Nincs megadva"}
                  </strong>
                </div>
              </div>

              <div className="travel-plan-item">
                <Bus size={16} />
                <div>
                  <span>Közlekedés</span>
                  <strong>
                    {formatTransportMode(travelPlan.transportMode)}
                  </strong>
                </div>
              </div>
            </div>
          </section>
        )}

        <main
          className="assistant-chat"
          aria-label="Beszélgetés"
          aria-live="polite"
        >
          {messages.map((message) => (
            <div
              key={message.id}
              className={`assistant-message ${
                message.role === "user" ? "user-message" : "ai-message"
              }`}
            >
              <div className="assistant-avatar">
                {message.role === "assistant" ? (
                  <Bot size={19} />
                ) : (
                  <UserRound size={19} />
                )}
              </div>

              <div className="assistant-message-content">
                <div className="assistant-message-label">
                  {message.role === "assistant" ? "DayPlannerAI" : "Te"}

                  <time>{formatTime(message.createdAt)}</time>
                </div>

                <div className="assistant-message-bubble">
                  {message.content}
                  {message.role === "assistant" &&
                    message.availableRoutes &&
                    message.availableRoutes.length > 0 && (
                      <TravelRouteCards routes={message.availableRoutes} />
                    )}
                  {message.role === "assistant" &&
                    message.availableJourneys &&
                    message.availableJourneys.length > 0 && (
                      <TravelJourneyCards
                        journeys={message.availableJourneys}
                      />
                    )}
                </div>
              </div>
            </div>
          ))}

          {isSending && (
            <div className="assistant-message ai-message">
              <div className="assistant-avatar">
                <Bot size={19} />
              </div>

              <div className="assistant-message-content">
                <div className="assistant-message-label">DayPlannerAI</div>

                <div className="assistant-typing">
                  <LoaderCircle size={17} className="assistant-spinner" />
                  Válasz készítése...
                </div>
              </div>
            </div>
          )}

          <div ref={messagesEndRef} />
        </main>

        {error && (
          <div className="assistant-error" role="alert">
            {error}
          </div>
        )}

        <footer className="assistant-footer">
          <form className="assistant-input-form" onSubmit={handleSubmit}>
            <textarea
              ref={textareaRef}
              value={input}
              onChange={(event) => setInput(event.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Írd ide az üzeneted..."
              rows={1}
              maxLength={4000}
              disabled={isSending}
              aria-label="Üzenet"
            />

            <button
              type="submit"
              disabled={!input.trim() || isSending}
              className="assistant-send-button"
              title="Üzenet küldése"
            >
              <Send size={19} />
            </button>
          </form>

          <div className="assistant-input-hint">
            <span>
              <Sparkles size={13} />
              Helyi AI-modell · Qwen 3.5 9B
            </span>

            <span>Enter: küldés · Shift + Enter: új sor</span>
          </div>
        </footer>
      </div>
    </div>
  );
}
