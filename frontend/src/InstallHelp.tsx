import { useState } from "react";
export default function InstallHelp() {
  const [open, setOpen] = useState(false);
  return (
    <section className="install-help">
      <button
        className="text-button"
        type="button"
        onClick={() => setOpen(!open)}
      >
        Install on iPhone
      </button>
      {open && (
        <div className="panel">
          <h2>Add Workout to your Home Screen</h2>
          <ol>
            <li>Open this website in Safari.</li>
            <li>
              Tap Share, then Add to Home Screen (it may be under More). Enable
              Open as Web App if shown.
            </li>
            <li>Tap Add, then launch Workout from its new icon.</li>
            <li>
              Sign in and open Settings → Notifications → Enable notifications.
            </li>
          </ol>
          <p>
            Notifications require iOS 16.4 or later and your permission. Saved
            offline workouts sync when you reopen the app with internet access.
            Sync pending changes before removing the app or clearing website
            data.
          </p>
        </div>
      )}
    </section>
  );
}
