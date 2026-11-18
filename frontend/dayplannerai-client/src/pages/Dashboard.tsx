
import { useQuery } from "@tanstack/react-query";
import {
    Activity,
    CalendarDays,
    Clock,
    MapPin,
    Sparkles,
} from "lucide-react";

import { checkApiHealth, getProfile } from "../api/profileApi";

function Dashboard() {
    const profileQuery = useQuery({
        queryKey: ["profile"],
        queryFn: getProfile,
    });

    const healthQuery = useQuery({
        queryKey: ["api-health"],
        queryFn: checkApiHealth,
    });

    const today = new Intl.DateTimeFormat("hu-HU", {
        weekday: "long",
        year: "numeric",
        month: "long",
        day: "numeric",
    }).format(new Date());

    return (
        <div className="page">
            <div className="page-heading">
                <div>
                    <span className="page-eyebrow">{today}</span>

                    <h1>
                        {profileQuery.data
                            ? `Üdvözöllek, ${profileQuery.data.displayName}!`
                            : "Üdvözöllek a DayPlannerAI-ban!"}
                    </h1>

                    <p>Tervezzük meg együtt a mai napodat!</p>
                </div>

                <div className="system-status">
                    <span
                        className={`status-dot ${
                            healthQuery.isSuccess ? "online" : "offline"
                        }`}
                    />
                    {healthQuery.isLoading
                        ? "Kapcsolódás..."
                        : healthQuery.isSuccess
                            ? "Minden rendszer működik"
                            : "A szerver nem elérhető"}
                </div>
            </div>

            <section className="welcome-panel">
                <div>
                    <div className="panel-label">
                        <Sparkles size={16} />
                        SZEMÉLYI AI-ASSZISZTENSED
                    </div>

                    <h2>A napod, intelligensen megszervezve.</h2>

                    <p>
                        A személyi asszisztensed segít a napi programod,
                        utazásaid és teendőid megtervezésében.
                    </p>
                </div>

                <div className="welcome-decoration">
                    <Sparkles size={64} />
                </div>
            </section>

            <section className="section">
                <div className="section-heading">
                    <h2>Napi áttekintés</h2>
                    <span>A mai nap legfontosabb információi</span>
                </div>

                <div className="overview-grid">
                    <article className="overview-card">
                        <div className="card-icon purple">
                            <CalendarDays size={21} />
                        </div>

                        <h3>Mai események</h3>
                        <strong>0</strong>
                        <span>A naptár összekapcsolása hamarosan</span>
                    </article>

                    <article className="overview-card">
                        <div className="card-icon blue">
                            <Clock size={21} />
                        </div>

                        <h3>Következő indulás</h3>
                        <strong>--:--</strong>
                        <span>A közlekedési modul hamarosan elérhető</span>
                    </article>

                    <article className="overview-card">
                        <div className="card-icon orange">
                            <MapPin size={21} />
                        </div>

                        <h3>Kiindulási hely</h3>
                        <strong>
                            {profileQuery.data?.homeLocation ?? "Nincs beállítva"}
                        </strong>
                        <span>A helyszínt a Beállításokban adhatod meg</span>
                    </article>

                    <article className="overview-card">
                        <div className="card-icon green">
                            <Activity size={21} />
                        </div>

                        <h3>Rendszerállapot</h3>
                        <strong>
                            {healthQuery.isSuccess ? "Online" : "Offline"}
                        </strong>
                        <span>
                            {healthQuery.isSuccess
                                ? "A szerverkapcsolat aktív"
                                : "A szerverkapcsolat nem elérhető"}
                        </span>
                    </article>
                </div>
            </section>

            <section className="section">
                <div className="section-heading">
                    <h2>Első lépések</h2>
                    <span>Az alkalmazás beállítása</span>
                </div>

                <div className="setup-card">
                    <div className="setup-item">
                        <span className="setup-check">✓</span>
                        <div>
                            <strong>Backend csatlakoztatva</strong>
                            <p>Az ASP.NET Core API működik.</p>
                        </div>
                    </div>

                    <div className="setup-item">
                        <span className="setup-check">✓</span>
                        <div>
                            <strong>Adatbázis csatlakoztatva</strong>
                            <p>A PostgreSQL-adatok elérhetők.</p>
                        </div>
                    </div>

                    <div className="setup-item pending">
                        <span className="setup-pending">3</span>
                        <div>
                            <strong>Google Naptár összekapcsolása</strong>
                            <p>A naptáresemények szinkronizálása.</p>
                        </div>
                    </div>

                    <div className="setup-item pending">
                        <span className="setup-pending">4</span>
                        <div>
                            <strong>AI-asszisztens konfigurálása</strong>
                            <p>Intelligens napi tervezés engedélyezése.</p>
                        </div>
                    </div>
                </div>
            </section>
        </div>
    );
}

export default Dashboard;
