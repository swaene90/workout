import { useEffect, useState } from "react";
import { api, demoMode } from "./api";
import {
  pending,
  sync,
  resolve,
  lastRefresh,
  syncError,
  type Pending,
} from "./offline";
import InstallHelp from "./InstallHelp";

type Preferences = {
  reminders: boolean;
  partner: boolean;
  celebration: boolean;
  nudge: boolean;
  reminderTime: string;
  reminderDays: string;
  quietStart: string;
  quietEnd: string;
};
const defaults: Preferences = {
  reminders: true,
  partner: true,
  celebration: true,
  nudge: true,
  reminderTime: "18:00",
  reminderDays: "0,1,2,3,4,5,6",
  quietStart: "21:00",
  quietEnd: "08:00",
};
export async function detachPush() {
  if (!("serviceWorker" in navigator)) return;
  const registration = await navigator.serviceWorker.getRegistration();
  const subscription = await registration?.pushManager?.getSubscription();
  if (subscription) {
    await api("/notifications/subscriptions", "DELETE", {
      endpoint: subscription.endpoint,
    });
    await subscription.unsubscribe();
  }
}
export function SyncStatus({
  onRefresh,
  editing,
}: {
  onRefresh: () => void;
  editing: boolean;
}) {
  const [items, setItems] = useState<Pending[]>([]);
  const [online, setOnline] = useState(navigator.onLine);
  const [refreshed, setRefreshed] = useState<number>();
  const [update, setUpdate] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    if (demoMode) return;
    const load = () => {
      void pending().then(setItems);
      void lastRefresh().then(setRefreshed);
      void syncError().then(setError);
    };
    const connection = () => {
      setOnline(navigator.onLine);
      load();
      if (navigator.onLine)
        void sync()
          .then(onRefresh)
          .catch(() => {});
    };
    const foreground = () => {
      if (document.visibilityState === "visible") connection();
    };
    const ready = () => setUpdate(true);
    window.addEventListener("workout-sync", load);
    window.addEventListener("online", connection);
    window.addEventListener("offline", connection);
    document.addEventListener("visibilitychange", foreground);
    window.addEventListener("workout-update", ready);
    const timer = setInterval(() => {
      if (document.visibilityState === "visible" && navigator.onLine)
        void sync()
          .then(onRefresh)
          .catch(() => {});
    }, 30000);
    load();
    void sync()
      .then(onRefresh)
      .catch(() => {});
    // Also detect an update that became ready before this component mounted.
    if ("serviceWorker" in navigator)
      void navigator.serviceWorker.getRegistration().then((r) => {
        if (r?.waiting) setUpdate(true);
      });
    return () => {
      clearInterval(timer);
      window.removeEventListener("workout-sync", load);
      window.removeEventListener("online", connection);
      window.removeEventListener("offline", connection);
      document.removeEventListener("visibilitychange", foreground);
      window.removeEventListener("workout-update", ready);
    };
  }, [onRefresh]);
  if (demoMode) return null;
  return (
    <div className="sync-status" aria-live="polite">
      {!online && (
        <p>
          Offline — showing saved data
          {refreshed ? ` from ${new Date(refreshed).toLocaleString()}` : ""}.
          Workout changes will sync when you reopen with internet.
        </p>
      )}
      {!!items.length && (
        <p>
          {items.length} pending change(s). Progress is provisional.{" "}
          <button
            className="text-button"
            disabled={!online}
            onClick={() =>
              void sync()
                .then(onRefresh)
                .catch(() => {})
            }
          >
            Retry sync
          </button>
        </p>
      )}
      {items
        .filter((p) => p.error)
        .map((p) => (
          <div className="panel" key={p.mutationId}>
            <h3>
              {p.workout?.title ?? p.baseline?.title ?? "Workout"}: needs
              attention
            </h3>
            <p>{p.error}</p>
            <p>
              Pending:{" "}
              {p.operation === "delete"
                ? "Delete workout"
                : `${p.workout?.date} · ${p.workout?.title}`}
            </p>
            {"conflict" in p && (
              <>
                <p>
                  Server:{" "}
                  {p.conflict
                    ? `${p.conflict.date} · ${p.conflict.title}`
                    : "Workout deleted"}
                </p>
                <details>
                  <summary>Compare details</summary>
                  <pre>
                    {JSON.stringify(
                      { pending: p.workout, server: p.conflict },
                      null,
                      2,
                    )}
                  </pre>
                </details>
              </>
            )}
            <button
              className="button secondary"
              disabled={!online}
              onClick={() =>
                void resolve(p, false)
                  .then(onRefresh)
                  .catch((e) => setError(e.message))
              }
            >
              Keep server version
            </button>{" "}
            <button
              className="button primary"
              disabled={!online}
              onClick={() =>
                void resolve(p, true)
                  .then(onRefresh)
                  .catch((e) => setError(e.message))
              }
            >
              {p.conflict === null && p.operation !== "delete"
                ? "Save as new workout"
                : "Apply my changes"}
            </button>
            <p>
              For invalid data, edit the pending workout in Workouts before
              applying again.
            </p>
          </div>
        ))}
      {error && (
        <p className="error">
          {error} <a href="/auth/login">Sign in again</a>
        </p>
      )}
      {update && (
        <p>
          App update available.{" "}
          <button
            className="text-button"
            disabled={editing}
            onClick={() => void import("./pwa").then((m) => m.updateApp(true))}
          >
            {editing ? "Finish editing to update" : "Update app"}
          </button>
        </p>
      )}
    </div>
  );
}
export default function NotificationSettings() {
  const [p, setP] = useState(defaults);
  const [available, setAvailable] = useState(false);
  const [enabled, setEnabled] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [key, setKey] = useState("");
  const supported =
    "serviceWorker" in navigator &&
    "PushManager" in window &&
    "Notification" in window;
  const permission = supported ? Notification.permission : "unsupported";
  useEffect(() => {
    if (demoMode) return;
    void Promise.all([
      api<{ available: boolean; publicKey: string }>("/notifications/config"),
      api<Preferences>("/notifications/preferences"),
    ])
      .then(async ([c, preferences]) => {
        setAvailable(c.available);
        setKey(c.publicKey);
        setP({ ...defaults, ...preferences });
        if (supported) {
          const registration = await navigator.serviceWorker.getRegistration();
          const sub = await registration?.pushManager.getSubscription();
          setEnabled(!!sub);
          if (sub && c.available && Notification.permission === "granted")
            await api("/notifications/subscriptions", "POST", sub.toJSON());
        }
      })
      .catch((e) => setMessage(e.message));
  }, []);
  const action = async (fn: () => Promise<void>) => {
    setBusy(true);
    setMessage("");
    try {
      await fn();
    } catch (e) {
      setMessage((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  const enable = async () => {
    // Request permission directly from the click, before awaiting other operations.
    const granted = await Notification.requestPermission();
    if (granted !== "granted") {
      setMessage(
        "Notifications are blocked. You can change permission in your device's notification settings.",
      );
      return;
    }
    const registration = await navigator.serviceWorker.ready;
    const bytes = Uint8Array.from(
      atob(
        key
          .replace(/-/g, "+")
          .replace(/_/g, "/")
          .padEnd(Math.ceil(key.length / 4) * 4, "="),
      ),
      (c) => c.charCodeAt(0),
    );
    const sub =
      (await registration.pushManager.getSubscription()) ??
      (await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: bytes,
      }));
    await api("/notifications/subscriptions", "POST", sub.toJSON());
    setEnabled(true);
    setMessage("Notifications enabled on this device.");
  };
  return (
    <section className="panel settings-panel">
      <h2>Notifications</h2>
      <InstallHelp />
      <p>
        Install on your iPhone Home Screen to receive notifications. Permission:{" "}
        {permission}.{" "}
        {enabled
          ? "This device is subscribed."
          : "This device is not subscribed."}
      </p>
      {demoMode ? (
        <p>Notifications are unavailable in the demo.</p>
      ) : (
        !available && (
          <p>
            Notifications are unavailable until the server's push keys are
            configured.
          </p>
        )
      )}
      {supported && available && !demoMode && (
        <button
          className="button secondary"
          disabled={busy || !navigator.onLine}
          onClick={() =>
            void action(
              enabled
                ? async () => {
                    await detachPush();
                    setEnabled(false);
                  }
                : enable,
            )
          }
        >
          {enabled ? "Disable on this device" : "Enable notifications"}
        </button>
      )}
      <form
        onSubmit={(e) => {
          e.preventDefault();
          void action(async () => {
            await api("/notifications/preferences", "PUT", p);
            setMessage("Notification preferences saved.");
          });
        }}
      >
        {(
          [
            ["reminders", "Workout reminders"],
            ["partner", "Partner check-ins"],
            ["celebration", "Weekly goal celebrations"],
            ["nudge", "Sunday progress nudge (5 PM)"],
          ] as const
        ).map(([field, label]) => (
          <label className="checkbox" key={field}>
            <input
              type="checkbox"
              checked={p[field]}
              onChange={(e) => setP({ ...p, [field]: e.target.checked })}
            />
            {label}
          </label>
        ))}
        <label>
          Reminder time (New York)
          <input
            type="time"
            required
            value={p.reminderTime}
            onChange={(e) => setP({ ...p, reminderTime: e.target.value })}
          />
        </label>
        <fieldset>
          <legend>Reminder days</legend>
          {["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"].map((day, i) => (
            <label className="checkbox" key={day}>
              <input
                type="checkbox"
                checked={p.reminderDays.split(",").includes(String(i))}
                onChange={(e) => {
                  const days = new Set(
                    p.reminderDays.split(",").filter(Boolean),
                  );
                  if (e.target.checked) days.add(String(i));
                  else days.delete(String(i));
                  setP({ ...p, reminderDays: [...days].sort().join(",") });
                }}
              />
              {day}
            </label>
          ))}
        </fieldset>
        <label>
          Quiet hours start
          <input
            type="time"
            required
            value={p.quietStart}
            onChange={(e) => setP({ ...p, quietStart: e.target.value })}
          />
        </label>
        <label>
          Quiet hours end
          <input
            type="time"
            required
            value={p.quietEnd}
            onChange={(e) => setP({ ...p, quietEnd: e.target.value })}
          />
        </label>
        <p className="muted">
          All times use New York time. Equal quiet-hour times turn quiet hours
          off. Offline check-ins reach your partner after syncing; reminders use
          confirmed server progress.
        </p>
        <button
          className="button primary"
          disabled={busy || demoMode || !navigator.onLine}
        >
          Save notification preferences
        </button>
      </form>
      {message && <p role="status">{message}</p>}
    </section>
  );
}
