import { useEffect, useState } from "react";
import { Clock, Globe2 } from "lucide-react";

interface ClockLocation {
    city: string;
    country: string;
    timezone: string;
    flag: string;
}

const clockLocations: ClockLocation[] = [
    {
        city: "Budapest",
        country: "Magyarország",
        timezone: "Europe/Budapest",
        flag: "🇭🇺"
    },
    {
        city: "London",
        country: "Egyesült Királyság",
        timezone: "Europe/London",
        flag: "🇬🇧"
    },
    {
        city: "New York",
        country: "USA",
        timezone: "America/New_York",
        flag: "🇺🇸"
    },
    {
        city: "Tokió",
        country: "Japán",
        timezone: "Asia/Tokyo",
        flag: "🇯🇵"
    }
];

function WorldClock() {
    const [now, setNow] = useState(() => new Date());

    useEffect(() => {
        const interval = window.setInterval(() => {
            setNow(new Date());
        }, 1000);

        return () => window.clearInterval(interval);
    }, []);

    return (
        <section className="section">
            <div className="section-heading">
                <h2>Világóra</h2>
                <span>
                    <Clock size={15} />
                    Élő helyi idő
                </span>
            </div>

            <div className="world-clock-grid">
                {clockLocations.map((location) => {
                    const time = new Intl.DateTimeFormat("hu-HU", {
                        hour: "2-digit",
                        minute: "2-digit",
                        second: "2-digit",
                        hourCycle: "h23",
                        timeZone: location.timezone
                    }).format(now);

                    const date = new Intl.DateTimeFormat("hu-HU", {
                        weekday: "long",
                        month: "long",
                        day: "numeric",
                        timeZone: location.timezone
                    }).format(now);

                    return (
                        <article
                            className="world-clock-card"
                            key={location.timezone}
                        >
                            <div className="world-clock-location">
                                <span className="world-clock-flag">
                                    {location.flag}
                                </span>

                                <div>
                                    <h3>{location.city}</h3>
                                    <span>{location.country}</span>
                                </div>
                            </div>

                            <div className="world-clock-time">
                                {time}
                            </div>

                            <div className="world-clock-date">
                                {date}
                            </div>
                        </article>
                    );
                })}
            </div>
        </section>
    );
}

export default WorldClock;