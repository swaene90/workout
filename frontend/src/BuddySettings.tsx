import { useEffect, useState } from "react";
import { request } from "./api";
import type { BuddyList, InvitationLink } from "./types";

export default function BuddySettings({
  onChange,
}: {
  onChange: () => Promise<void>;
}) {
  const [list, setList] = useState<BuddyList>({ buddies: [], invitations: [] });
  const [email, setEmail] = useState("");
  const [link, setLink] = useState<InvitationLink | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);
  const load = async () => setList(await request<BuddyList>("/buddies"));
  useEffect(() => {
    void load().catch((e) => setError(e.message));
    const refresh = () => {
      if (navigator.onLine && !document.hidden)
        void load().catch((e) => setError(e.message));
    };
    window.addEventListener("online", refresh);
    document.addEventListener("visibilitychange", refresh);
    return () => {
      window.removeEventListener("online", refresh);
      document.removeEventListener("visibilitychange", refresh);
    };
  }, []);
  async function run(operation: () => Promise<unknown>) {
    setBusy(true);
    setError("");
    setCopied(false);
    try {
      await operation();
      await load();
      await onChange();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <section
      className="panel settings-panel buddy-settings"
      aria-label="Accountability buddies"
    >
      <h2>Accountability buddies</h2>
      <p>
        Invite friends to share your workout history and progress. Each buddy
        connects only with you.
      </p>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          void run(async () => {
            setLink(
              await request<InvitationLink>("/buddy-invitations", "POST", {
                email,
              }),
            );
            setEmail("");
          });
        }}
      >
        <label htmlFor="buddy-email">Friend’s Google account email</label>
        <input
          id="buddy-email"
          type="email"
          required
          maxLength={320}
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
        <button className="button primary" disabled={busy}>
          Invite a friend
        </button>
      </form>
      {error && <p role="alert">{error}</p>}
      {link && (
        <div className="buddy-link">
          <label htmlFor="buddy-link">
            Share this invitation with {link.email}
          </label>
          <input
            id="buddy-link"
            readOnly
            value={link.link}
            onFocus={(e) => e.target.select()}
          />
          <p>Expires {new Date(link.expires).toLocaleDateString()}.</p>
          <button
            className="button secondary"
            onClick={async () => {
              try {
                await navigator.clipboard.writeText(link.link);
                setCopied(true);
              } catch {
                setError("Select and copy the invitation link above.");
              }
            }}
          >
            {copied ? "Copied" : "Copy invitation link"}
          </button>
        </div>
      )}
      <h3>Your buddies</h3>
      {!list.buddies.length && (
        <p>No buddies yet. Invite a friend to get started.</p>
      )}
      {list.buddies.map((b) => (
        <div className="buddy-row" key={b.id}>
          <span>{b.name}</span>
          <button
            className="button secondary"
            disabled={busy}
            onClick={() => {
              if (
                window.confirm(
                  `Remove ${b.name}? You will stop sharing activity with each other.`,
                )
              )
                void run(() => request(`/buddies/${b.id}`, "DELETE"));
            }}
          >
            Remove buddy
          </button>
        </div>
      ))}
      {!!list.invitations.length && <h3>Invitations</h3>}
      {list.invitations.map((i) => (
        <div className="buddy-row invitation-row" key={i.id}>
          <span>
            {i.email} · {i.status === "pending" ? "Pending" : "Expired"}
          </span>
          <button
            className="button secondary"
            disabled={busy}
            onClick={() =>
              void run(async () => {
                setLink(
                  await request<InvitationLink>(
                    `/buddy-invitations/${i.id}/replace`,
                    "POST",
                  ),
                );
              })
            }
          >
            Replace link
          </button>
          <button
            className="button secondary"
            disabled={busy}
            onClick={() => {
              if (window.confirm(`Cancel the invitation to ${i.email}?`))
                void run(async () => {
                  await request(`/buddy-invitations/${i.id}`, "DELETE");
                  if (link?.id === i.id) setLink(null);
                });
            }}
          >
            Cancel invitation
          </button>
        </div>
      ))}
    </section>
  );
}
