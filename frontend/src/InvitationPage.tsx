import { useEffect, useState } from "react";
import { request, setCsrf } from "./api";
import type { InvitationPreview } from "./types";

export default function InvitationPage({ token }: { token: string }) {
  const [preview, setPreview] = useState<InvitationPreview | null>(null);
  const [error, setError] = useState(
    location.search.includes("authError=1")
      ? "Google sign-in failed. Use the email your friend invited and try again."
      : "",
  );
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    request<InvitationPreview>(
      `/buddy-invitations/preview?token=${encodeURIComponent(token)}`,
    )
      .then((p) => {
        setPreview(p);
        setCsrf(p.csrfToken);
      })
      .catch(() =>
        setError(
          "This invitation is unavailable. Ask your friend for a new link.",
        ),
      );
  }, [token]);
  async function signOut(decline = false) {
    setBusy(true);
    try {
      const response = await fetch(
        `/auth/invitation/logout?member=${!decline}`,
        { method: "POST", headers: { "X-CSRF-TOKEN": preview!.csrfToken } },
      );
      if (!response.ok)
        throw new Error("Could not sign out. Refresh and try again.");
      if (decline) location.assign("/");
      else location.reload();
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }
  return (
    <main className="invitation-page">
      <section className="panel settings-panel">
        <h1>Workout buddy invitation</h1>
        {error && <p role="alert">{error}</p>}
        {!preview && !error && <p>Loading invitation…</p>}
        {preview && (
          <>
            <h2>
              {preview.inviterName} invited you to be accountability buddies.
            </h2>
            <p>
              Accepting shares your past and future workouts, notes, streaks,
              and exercise progress with each other. You connect only with this
              person.
            </p>
            {preview.status !== "pending" ? (
              <p>
                This invitation is {preview.status}. Ask your friend for a new
                link if you want to connect.
              </p>
            ) : !preview.authenticated ? (
              <a
                className="button primary"
                href={`/auth/login?invite=${encodeURIComponent(token)}`}
              >
                Continue with Google
              </a>
            ) : !preview.matchesEmail ? (
              <>
                <p>Sign in with the Google email your friend invited.</p>
                <button
                  className="button secondary"
                  disabled={busy}
                  onClick={() => void signOut()}
                >
                  Sign out and try another account
                </button>
              </>
            ) : (
              <>
                <button
                  className="button primary"
                  disabled={busy}
                  onClick={async () => {
                    setBusy(true);
                    setError("");
                    try {
                      await request("/buddy-invitations/accept", "POST", {
                        token,
                      });
                      location.assign("/");
                    } catch (e) {
                      setError((e as Error).message);
                      setBusy(false);
                    }
                  }}
                >
                  Accept invitation
                </button>
                <button
                  className="button secondary"
                  disabled={busy}
                  onClick={async () => {
                    await signOut(true);
                  }}
                >
                  Decline
                </button>
              </>
            )}
          </>
        )}
        <p>
          <a href="/">Return to Workout</a>
        </p>
      </section>
    </main>
  );
}
