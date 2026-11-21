
import { BrowserRouter, Routes, Route } from "react-router-dom";

import AppLayout from "./components/layout/AppLayout";

import Dashboard from "./pages/Dashboard";
import Calendar from "./pages/Calendar";
import Travel from "./pages/Travel";
import Settings from "./pages/Settings";
import Assistant from "./pages/Assistant";

function App() {
    return (
        <BrowserRouter>
            <Routes>
                <Route element={<AppLayout />}>
                    <Route path="/" element={<Dashboard />} />
                    <Route path="/calendar" element={<Calendar />} />
                    <Route path="/travel" element={<Travel />} />
                    <Route path="/settings" element={<Settings />} />
                    <Route path="/assistant" element={<Assistant />} />
                </Route>
            </Routes>
        </BrowserRouter>
    );
}

export default App;
