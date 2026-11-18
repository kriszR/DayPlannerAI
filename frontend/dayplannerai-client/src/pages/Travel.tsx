
import { Map } from "lucide-react";

function Travel() {
    return (
        <div className="page">
            <div className="page-heading">
                <div>
                    <span className="page-eyebrow">KÖZLEKEDÉS</span>
                    <h1>Utazás</h1>
                    <p>Tervezd meg az útvonalaidat és napi indulásaidat.</p>
                </div>
            </div>

            <div className="empty-state">
                <Map size={42} />
                <h2>Utazási asszisztens</h2>
                <p>
                    A tömegközlekedési menetrendek és útvonaltervezés
                    a későbbi fejlesztési lépésekben lesz elérhető.
                </p>
            </div>
        </div>
    );
}

export default Travel;
