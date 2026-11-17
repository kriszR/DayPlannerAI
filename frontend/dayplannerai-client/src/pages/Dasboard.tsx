import { useQuery } from "@tanstack/react-query";
import { Activity, User, MapPin, Clock } from "lucide-react";

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

  return (
    <main className="dashboard">
      <header className="dashboard-header">
        <div>
          <p className="eyebrow">PERSONAL AI ASSISTANT</p>
          <h1>DayPlannerAI</h1>
          <p className="subtitle">Your personal daily planning assistant.</p>
        </div>

        <div className="status">
          <span
            className={`status-dot ${
              healthQuery.isSuccess ? "online" : "offline"
            }`}
          />
          {healthQuery.isLoading
            ? "Connecting..."
            : healthQuery.isSuccess
              ? "Backend connected"
              : "Backend unavailable"}
        </div>
      </header>

      <section className="welcome">
        <h2>
          {profileQuery.data
            ? `Hello, ${profileQuery.data.displayName}!`
            : "Welcome to DayPlannerAI"}
        </h2>

        <p>Your day, organized intelligently.</p>
      </section>

      <section className="cards">
        <article className="info-card">
          <User size={22} />
          <h3>Profile</h3>

          {profileQuery.isLoading && <p>Loading profile...</p>}

          {profileQuery.isError && (
            <p className="error">Could not load the profile.</p>
          )}

          {profileQuery.data && (
            <>
              <p>{profileQuery.data.displayName}</p>
              <small>User ID: {profileQuery.data.id}</small>
            </>
          )}
        </article>

        <article className="info-card">
          <MapPin size={22} />
          <h3>Location</h3>
          <p>{profileQuery.data?.homeLocation ?? "Not configured"}</p>
        </article>

        <article className="info-card">
          <Clock size={22} />
          <h3>Timezone</h3>
          <p>{profileQuery.data?.timeZone ?? "Loading..."}</p>
        </article>

        <article className="info-card">
          <Activity size={22} />
          <h3>System status</h3>
          <p>{healthQuery.data?.status ?? "Waiting for API..."}</p>
        </article>
      </section>
    </main>
  );
}

export default Dashboard;
