
import { NavLink } from "react-router-dom";
import {
    LayoutDashboard,
    CalendarDays,
    Map,
    Settings,
    Sparkles,
} from "lucide-react";

const navigation = [
    {
        label: "Áttekintés",
        path: "/",
        icon: LayoutDashboard,
    },
    {
        label: "Naptár",
        path: "/calendar",
        icon: CalendarDays,
    },
    {
        label: "Utazás",
        path: "/travel",
        icon: Map,
    },
    {
        label: "Beállítások",
        path: "/settings",
        icon: Settings,
    },
];

function Sidebar() {
    return (
        <aside className="sidebar">
            <div className="brand">
                <div className="brand-icon">
                    <Sparkles size={22} />
                </div>

                <div>
                    <h2>DayPlannerAI</h2>
                    <span>Személyi asszisztens</span>
                </div>
            </div>

            <nav className="navigation">
                <span className="nav-heading">MENÜ</span>

                {navigation.map((item) => {
                    const Icon = item.icon;

                    return (
                        <NavLink
                            key={item.path}
                            to={item.path}
                            end={item.path === "/"}
                            className={({ isActive }) =>
                                `nav-link ${isActive ? "active" : ""}`
                            }
                        >
                            <Icon size={19} />
                            <span>{item.label}</span>
                        </NavLink>
                    );
                })}
            </nav>

            <div className="sidebar-footer">
                <div className="assistant-indicator" />
                <span>AI-asszisztens aktív</span>
            </div>
        </aside>
    );
}

export default Sidebar;
