
import { Outlet } from "react-router-dom";
import Sidebar from "./Sidebar";

function AppLayout() {
    return (
        <div className="app-layout">
            <Sidebar />

            <div className="main-container">
                <header className="topbar">
                    <span className="topbar-label">
                        Személyi napi asszisztensed
                    </span>
                </header>

                <div className="page-content">
                    <Outlet />
                </div>
            </div>
        </div>
    );
}

export default AppLayout;
