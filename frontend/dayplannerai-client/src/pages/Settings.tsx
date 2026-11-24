
import { useState } from "react";
import {
    useMutation,
    useQuery,
    useQueryClient,
} from "@tanstack/react-query";

import {
    getProfile,
    updateProfile,
    type UpdateProfileRequest,
} from "../api/profileApi";

function Settings() {
    const queryClient = useQueryClient();

    const { data, isLoading, isError } = useQuery({
        queryKey: ["profile"],
        queryFn: getProfile,
    });

    const [displayName, setDisplayName] = useState<string | null>(null);
    const [homeLocation, setHomeLocation] = useState<string | null>(null);
    const [timeZone, setTimeZone] = useState<string | null>(null);
    const [preparationMinutes, setPreparationMinutes] = useState<string | null>(
        null
    );

    const [successMessage, setSuccessMessage] = useState("");

    const updateMutation = useMutation({
        mutationFn: (profile: UpdateProfileRequest) =>
            updateProfile(profile),

        onSuccess: async () => {
            setSuccessMessage("A profil sikeresen mentve.");

            await queryClient.invalidateQueries({
                queryKey: ["profile"],
            });
        },

        onError: () => {
            setSuccessMessage("");
        },
    });

    function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
        event.preventDefault();
        setSuccessMessage("");

        const preparationMinutesInput =
            event.currentTarget.elements.namedItem("preparationMinutes");

        if (
            !(preparationMinutesInput instanceof HTMLInputElement) ||
            !preparationMinutesInput.reportValidity()
        ) {
            return;
        }

        updateMutation.mutate({
            displayName: (displayName ?? data!.displayName).trim(),
            homeLocation: (
                homeLocation ?? data!.homeLocation ?? ""
            ).trim() || null,
            timeZone: (timeZone ?? data!.timeZone).trim(),
            preparationMinutes: preparationMinutesInput.value
                ? preparationMinutesInput.valueAsNumber
                : null,
        });
    }

    return (
        <div className="page">
            <div className="page-heading">
                <div>
                    <span className="page-eyebrow">KONFIGURÁCIÓ</span>
                    <h1>Beállítások</h1>
                    <p>Kezeld a személyi asszisztensed beállításait.</p>
                </div>
            </div>

            <section className="settings-card">
                <h2>Felhasználói profil</h2>

                {isLoading && <p>Profiladatok betöltése...</p>}

                {isError && (
                    <p className="settings-error">
                        Nem sikerült betölteni a profilt.
                    </p>
                )}

                {data && (
                    <form
                        className="profile-form"
                        onSubmit={handleSubmit}
                    >
                        <div className="form-group">
                            <label htmlFor="displayName">
                                Megjelenített név
                            </label>

                            <input
                                id="displayName"
                                type="text"
                                value={displayName ?? data.displayName}
                                onChange={(event) =>
                                    setDisplayName(event.target.value)
                                }
                                minLength={2}
                                maxLength={100}
                                required
                            />
                        </div>

                        <div className="form-group">
                            <label htmlFor="homeLocation">
                                Kiindulási hely
                            </label>

                            <input
                                id="homeLocation"
                                type="text"
                                value={
                                    homeLocation ??
                                    data.homeLocation ??
                                    ""
                                }
                                onChange={(event) =>
                                    setHomeLocation(event.target.value)
                                }
                                maxLength={200}
                                placeholder="Például: Budapest"
                            />

                            <small>
                                Ezt használja majd az asszisztens az
                                időjárási és közlekedési információkhoz.
                            </small>
                        </div>

                        <div className="form-group">
                            <label htmlFor="timeZone">
                                Időzóna
                            </label>

                            <select
                                id="timeZone"
                                value={timeZone ?? data.timeZone}
                                onChange={(event) =>
                                    setTimeZone(event.target.value)
                                }
                                required
                            >
                                <option value="Europe/Budapest">
                                    Budapest (Europe/Budapest)
                                </option>

                                <option value="Europe/Vienna">
                                    Bécs (Europe/Vienna)
                                </option>

                                <option value="Europe/Berlin">
                                    Berlin (Europe/Berlin)
                                </option>

                                <option value="Europe/London">
                                    London (Europe/London)
                                </option>

                                <option value="UTC">
                                    UTC
                                </option>
                            </select>
                        </div>

                        <div className="form-group">
                            <label htmlFor="preparationMinutes">
                                Felkészülési idő indulás előtt (perc)
                            </label>

                            <input
                                id="preparationMinutes"
                                name="preparationMinutes"
                                type="number"
                                min={0}
                                max={180}
                                step={1}
                                value={
                                    preparationMinutes ??
                                    data.preparationMinutes?.toString() ??
                                    ""
                                }
                                onChange={(event) =>
                                    setPreparationMinutes(event.target.value)
                                }
                            />

                            <small>
                                Ennyi időt tervez be az asszisztens az indulás
                                előtti készülődésre (0–180 perc).
                            </small>
                        </div>

                        {successMessage && (
                            <p className="settings-success">
                                {successMessage}
                            </p>
                        )}

                        {updateMutation.isError && (
                            <p className="settings-error">
                                Nem sikerült menteni a profiladatokat.
                                Ellenőrizd a backend kapcsolatát.
                            </p>
                        )}

                        <button
                            type="submit"
                            disabled={updateMutation.isPending}
                        >
                            {updateMutation.isPending
                                ? "Mentés..."
                                : "Változtatások mentése"}
                        </button>
                    </form>
                )}
            </section>
        </div>
    );
}

export default Settings;
