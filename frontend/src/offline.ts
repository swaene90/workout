import { openDB } from "idb";
import type {
  Me,
  Workout,
  WorkoutInput,
  History,
  Dashboard,
  Goal,
} from "./types";
import { ApiError, setCsrf, setAccountIdentity, request } from "./api";

export type Pending = {
  mutationId: string;
  operation: "create" | "update" | "delete";
  workoutId: string;
  expectedRevision: number | null;
  workout: WorkoutInput | null;
  userId: string;
  queuedAt: number;
  frozen: boolean;
  error?: string;
  conflict?: Workout | null;
  baseline?: Workout;
};
const database = () =>
  openDB("workout-offline", 1, {
    upgrade(db) {
      db.createObjectStore("cache");
      db.createObjectStore("queue", { keyPath: "mutationId" });
      db.createObjectStore("meta");
    },
  });
let account = "";
let run: Promise<void> | undefined;
export const todayInNewYork = () =>
  new Intl.DateTimeFormat("en-CA", {
    timeZone: "America/New_York",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
export const changed = () => window.dispatchEvent(new Event("workout-sync"));
export async function pending(): Promise<Pending[]> {
  const db = await database();
  const all = await db.getAll("queue");
  return all
    .filter((p: Pending) => p.userId === account)
    .sort((a: Pending, b: Pending) => a.queuedAt - b.queuedAt);
}
async function exclusive<T>(fn: () => Promise<T>): Promise<T> {
  if (navigator.locks) return navigator.locks.request("workout-offline", fn);
  const db = await database();
  const owner = crypto.randomUUID();
  const tx = db.transaction("meta", "readwrite");
  const lease = await tx.store.get("lease");
  if (lease && lease.until > Date.now()) {
    await tx.done;
    throw new Error(
      "Workout synchronization is busy in another tab. Try again shortly.",
    );
  }
  await tx.store.put({ owner, until: Date.now() + 60000 }, "lease");
  await tx.done;
  const renewal = window.setInterval(() => {
    void db.put("meta", { owner, until: Date.now() + 60000 }, "lease");
  }, 10000);
  try {
    return await fn();
  } finally {
    clearInterval(renewal);
    const t = db.transaction("meta", "readwrite");
    if ((await t.store.get("lease"))?.owner === owner)
      await t.store.delete("lease");
    await t.done;
  }
}
export async function cachedWorkout(id: string): Promise<Workout | undefined> {
  const db = await database();
  const local = (await pending()).filter((p) => p.workoutId === id).at(-1);
  if (local?.operation === "delete") return;
  if (local?.workout)
    return {
      ...local.workout,
      id,
      userId: account,
      revision: local.expectedRevision ?? 1,
      pending: true,
    };
  return (await db.get("cache", `${account}:/workouts/${id}`))?.value;
}
const cacheable = (path: string) =>
  /^\/(dashboard|workouts|templates|goals|progress)(\?|\/|$)/.test(path);
export async function offlineRequest<T>(
  path: string,
  method: string,
  body?: unknown,
): Promise<T> {
  if (path === "/me" && method === "GET") {
    const db = await database();
    try {
      const me = await request<Me>(path);
      setCsrf(me.csrfToken);
      await exclusive(async () => {
        const last = await db.get("meta", "account");
        if (last && last !== me.user.id) {
          account = last;
          if ((await pending()).length)
            throw new Error(
              "Sign back in to your previous account to sync or discard pending workouts before switching accounts.",
            );
          await resetStores();
        }
        account = me.user.id;
        setAccountIdentity(account);
        await db.put("meta", account, "account");
        const { csrfToken: _csrf, ...safe } = me;
        await db.put("meta", { ...safe, csrfToken: "" }, "me");
      });
      return me as T;
    } catch (error) {
      if (error instanceof ApiError) {
        if (error.status === 401 || error.status === 403)
          await db.delete("meta", "me");
        throw error;
      }
      if (
        !(
          error instanceof TypeError ||
          error instanceof DOMException ||
          !navigator.onLine
        )
      )
        throw error;
      const me = await db.get("meta", "me");
      if (!me)
        throw new Error(
          "Connect to the internet and sign in before using workouts offline.",
        );
      account = me.user.id;
      setAccountIdentity(account);
      return { ...me, today: todayInNewYork() } as T;
    }
  }
  if (method !== "GET" && /^\/workouts(?:\/[a-f0-9-]+)?$/.test(path))
    return saveWorkout<T>(path, method, body as WorkoutInput);
  if (method !== "GET") {
    if (!navigator.onLine)
      throw new Error(
        "Connect to the internet to change templates or settings.",
      );
    return request<T>(path, method, body);
  }
  if (!cacheable(path) || !account) return request<T>(path);
  const db = await database();
  const requestAccount = account;
  const key = `${requestAccount}:${path}`;
  let value: unknown;
  try {
    value = await request<T>(path);
    if (
      account !== requestAccount ||
      (await db.get("meta", "account")) !== requestAccount
    )
      throw new ApiError(
        401,
        "Your account changed. Reload and sign in again.",
      );
    await db.put("cache", { value, at: Date.now() }, key);
    if (path === "/dashboard") {
      try {
        const snapshot = await request<{ week: string; workouts: Workout[] }>(
          "/offline-snapshot",
        );
        if (
          snapshot.week &&
          account === requestAccount &&
          (await db.get("meta", "account")) === requestAccount
        )
          await db.put(
            "cache",
            { value: snapshot.workouts, at: Date.now() },
            `${requestAccount}:/week/${snapshot.week}`,
          );
      } catch {
        /* Keep the loaded overview if the optional snapshot fails. */
      }
    }
    if (path.startsWith("/workouts")) {
      const items = Array.isArray((value as History)?.items)
        ? (value as History).items
        : [value as Workout];
      for (const w of items)
        if (w?.id)
          await db.put(
            "cache",
            { value: w, at: Date.now() },
            `${requestAccount}:/workouts/${w.id}`,
          );
    }
  } catch (error) {
    if (error instanceof ApiError) throw error;
    const cached = await db.get("cache", key);
    if (!cached) {
      if (path.startsWith("/workouts/")) {
        const w = await cachedWorkout(path.split("/")[2]);
        if (w) return w as T;
      }
      throw new Error(
        "This view has not been loaded yet. Connect to the internet to open it.",
      );
    }
    value = cached.value;
  }
  if (path.startsWith("/workouts/")) {
    const local = await cachedWorkout(path.split("/")[2]);
    if (local) return local as T;
  }
  if (path.startsWith("/workouts?") || path === "/workouts")
    value = await overlayHistory(value as History, path);
  if (path === "/dashboard")
    value = await overlayDashboard(
      rollWeek(value as Dashboard[], todayInNewYork()),
    );
  return value as T;
}
async function saveWorkout<T>(
  path: string,
  method: string,
  body: WorkoutInput & { expectedRevision?: number },
): Promise<T> {
  if (!account) throw new Error("Sign in before saving a workout.");
  const owner = account;
  const { expectedRevision, ...input } = body ?? {};
  const result = await exclusive(async () => {
    const db = await database();
    const id = path.split("/")[2] || crypto.randomUUID();
    if (account !== owner || (await db.get("meta", "account")) !== owner)
      throw new Error("Your account changed. Reload before saving workouts.");
    const entries = (await pending()).filter((p) => p.workoutId === id);
    const old = await cachedWorkout(id);
    if (old && old.userId !== account)
      throw new Error("You can only change your own workouts.");
    if (method !== "POST" && !old && !entries.length)
      throw new Error("Load this workout online before changing it offline.");
    const last = entries.at(-1);
    const operation =
      method === "POST" ? "create" : method === "DELETE" ? "delete" : "update";
    if (last && !last.frozen && !last.error) {
      if (last.operation === "create" && operation === "delete")
        await db.delete("queue", last.mutationId);
      else {
        last.workout = operation === "delete" ? null : input;
        if (last.operation !== "create") last.operation = operation;
        await db.put("queue", last);
      }
    } else {
      const p: Pending = {
        queuedAt: Math.max(
          Date.now(),
          ...(await pending()).map((p) => p.queuedAt + 1),
        ),
        mutationId: crypto.randomUUID(),
        operation,
        workoutId: id,
        expectedRevision:
          operation === "create"
            ? null
            : (expectedRevision ?? old?.revision ?? 1),
        workout: operation === "delete" ? null : input,
        userId: owner,
        frozen: false,
        baseline: old,
      };
      await db.put("queue", p);
    }
    return {
      ...input,
      id,
      userId: account,
      revision: old?.revision ?? 1,
      pending: true,
    };
  });
  changed();
  await sync();
  return result as T;
}
export async function sync(): Promise<void> {
  if (!account || !navigator.onLine) return;
  if (run) return run;
  run = exclusive(async () => {
    let me: Me;
    try {
      me = await request<Me>("/me");
    } catch (error) {
      if (
        error instanceof ApiError &&
        (error.status === 401 || error.status === 403)
      ) {
        const db = await database();
        await db.put(
          "meta",
          "Sign in to the same account to sync pending workouts.",
          "syncError",
        );
        changed();
      }
      return;
    }
    if (me.user.id !== account) {
      const db = await database();
      await db.put(
        "meta",
        "Sign in to the account that owns these pending workouts.",
        "syncError",
      );
      changed();
      return;
    }
    setCsrf(me.csrfToken);
    const db = await database();
    const blocked = new Set<string>();
    await db.delete("meta", "syncError");
    // Reconcile an existing subscription on foreground visits, without prompting.
    if (
      "serviceWorker" in navigator &&
      "Notification" in window &&
      Notification.permission === "granted"
    ) {
      try {
        const registration = await navigator.serviceWorker.getRegistration();
        const subscription = await registration?.pushManager?.getSubscription();
        if (subscription) {
          const config = await request<{ available: boolean }>(
            "/notifications/config",
          );
          if (config.available)
            await request(
              "/notifications/subscriptions",
              "POST",
              subscription.toJSON(),
            );
        }
      } catch {
        /* A push-service failure must not stop workout synchronization. */
      }
    }
    for (const queued of await pending()) {
      const p: Pending | undefined = await db.get("queue", queued.mutationId);
      if (!p) continue;
      if (p.error || blocked.has(p.workoutId)) {
        blocked.add(p.workoutId);
        continue;
      }
      p.frozen = true;
      await db.put("queue", p);
      try {
        const result = await request<Workout>("/workout-mutations", "POST", {
          mutationId: p.mutationId,
          operation: p.operation,
          workoutId: p.workoutId,
          expectedRevision: p.expectedRevision,
          workout: p.workout,
        });
        if ((await db.get("meta", "account")) !== account) return;
        const tx = db.transaction(["queue", "cache"], "readwrite");
        await tx.objectStore("queue").delete(p.mutationId);
        const key = `${account}:/workouts/${p.workoutId}`;
        if (p.operation === "delete") await tx.objectStore("cache").delete(key);
        else
          await tx
            .objectStore("cache")
            .put({ value: result, at: Date.now() }, key);
        const next = ((await tx.objectStore("queue").getAll()) as Pending[])
          .sort((a, b) => a.queuedAt - b.queuedAt)
          .find(
            (q) =>
              q.userId === account && q.workoutId === p.workoutId && !q.frozen,
          );
        if (next && p.operation !== "delete") {
          next.expectedRevision = result.revision;
          next.baseline = result;
          await tx.objectStore("queue").put(next);
        }
        await tx.done;
      } catch (error) {
        if (
          !(error instanceof ApiError) ||
          error.status >= 500 ||
          error.status === 401 ||
          error.status === 403
        )
          break;
        p.error = error.message;
        if (error.status === 409)
          p.conflict = (error.problem?.current as Workout | null) ?? null;
        await db.put("queue", p);
        blocked.add(p.workoutId);
      }
    }
    changed();
  })
    .catch(() => {})
    .finally(() => {
      run = undefined;
    });
  return run;
}
export async function resolve(p: Pending, apply: boolean) {
  await exclusive(async () => {
    const db = await database();
    const changes = (await pending()).filter(
      (q) => q.workoutId === p.workoutId,
    );
    const latest = changes.at(-1) ?? p;
    let current: Workout | null = null;
    if (apply) {
      try {
        current = await request<Workout>(`/workouts/${p.workoutId}`);
      } catch (e) {
        if (!(e instanceof ApiError && e.status === 404)) throw e;
      }
    }
    const tx = db.transaction(["queue", "cache"], "readwrite");
    for (const q of changes) await tx.objectStore("queue").delete(q.mutationId);
    const key = `${account}:/workouts/${p.workoutId}`;
    const server = apply ? current : p.conflict;
    if (server)
      await tx.objectStore("cache").put({ value: server, at: Date.now() }, key);
    else await tx.objectStore("cache").delete(key);
    if (apply && (current || latest.operation !== "delete")) {
      const id = current ? p.workoutId : crypto.randomUUID();
      await tx.objectStore("queue").put({
        ...latest,
        mutationId: crypto.randomUUID(),
        workoutId: id,
        operation: current
          ? latest.operation === "delete"
            ? "delete"
            : "update"
          : "create",
        expectedRevision: current?.revision ?? null,
        frozen: false,
        error: undefined,
        conflict: undefined,
        baseline: current ?? undefined,
      });
    }
    await tx.done;
  });
  changed();
  await sync();
}
export async function clearLocal() {
  if (run) await run;
  await exclusive(resetStores);
}
async function resetStores() {
  const db = await database();
  const tx = db.transaction(["cache", "queue", "meta"], "readwrite");
  const lease = await tx.objectStore("meta").get("lease");
  await Promise.all([
    tx.objectStore("cache").clear(),
    tx.objectStore("queue").clear(),
    tx.objectStore("meta").clear(),
  ]);
  if (lease) await tx.objectStore("meta").put(lease, "lease");
  await tx.done;
  account = "";
  setAccountIdentity("");
  changed();
  localStorage.setItem("workout-account-change", crypto.randomUUID());
}
async function overlayHistory(h: History, path: string): Promise<History> {
  const query = new URLSearchParams(path.split("?")[1]);
  const matches = (w: Workout) =>
    (!query.get("userId") || query.get("userId") === w.userId) &&
    (!query.get("from") || w.date >= query.get("from")!) &&
    (!query.get("to") || w.date <= query.get("to")!);
  const items = new Map(h.items.map((w) => [w.id, w]));
  let total = h.total;
  for (const p of await pending()) {
    const previous = items.get(p.workoutId) ?? p.baseline;
    const before = previous && matches(previous);
    items.delete(p.workoutId);
    const w = p.workout
      ? {
          ...p.workout,
          id: p.workoutId,
          userId: account,
          revision: p.expectedRevision ?? 1,
          pending: true,
        }
      : null;
    const after = w && matches(w);
    if (after && (h.page === 1 || before)) items.set(w.id, w);
    total += (after ? 1 : 0) - (before ? 1 : 0);
  }
  return {
    ...h,
    total: Math.max(0, total),
    items: [...items.values()].sort(
      (a, b) => b.date.localeCompare(a.date) || a.id.localeCompare(b.id),
    ),
  };
}
async function overlayDashboard(dashboard: Dashboard[]): Promise<Dashboard[]> {
  const changes = await pending();
  if (!changes.length) return dashboard;
  const db = await database();
  const goals: Goal[] =
    (await db.get("cache", `${account}:/goals`))?.value ?? [];
  const snapshots = await db.getAllKeys("cache");
  const records: Workout[] = [];
  for (const k of snapshots)
    if (String(k).startsWith(`${account}:/workouts/`))
      records.push((await db.get("cache", k))?.value);
  const effective = new Map(records.filter(Boolean).map((w) => [w.id, w]));
  for (const p of changes) {
    if (p.workout)
      effective.set(p.workoutId, {
        ...p.workout,
        id: p.workoutId,
        userId: account,
        revision: 1,
      });
    else effective.delete(p.workoutId);
  }
  const own = dashboard.find((d) => d.user.id === account);
  const weekWorkouts: Workout[] | undefined = own
    ? (await db.get("cache", `${account}:/week/${own.summary.weekStart}`))
        ?.value
    : undefined;
  const weekEffective = new Map((weekWorkouts ?? []).map((w) => [w.id, w]));
  for (const p of changes) {
    if (p.workout)
      weekEffective.set(p.workoutId, {
        ...p.workout,
        id: p.workoutId,
        userId: account,
        revision: p.expectedRevision ?? 1,
      });
    else weekEffective.delete(p.workoutId);
  }
  return dashboard.map((d) => {
    if (d.user.id !== account) return d;
    const summary = structuredClone(d.summary);
    summary.calendar = summary.calendar.map((day) => {
      const related = changes.filter(
        (p) => p.workout?.date === day.date || p.baseline?.date === day.date,
      );
      if (!related.length) return day;
      const completed = [
        ...(weekWorkouts ? weekEffective : effective).values(),
      ].some((w) => w.userId === account && w.completed && w.date === day.date);
      return { ...day, workedOut: completed };
    });
    summary.workoutDaysThisWeek = summary.calendar.filter(
      (day) => day.workedOut,
    ).length;
    summary.weeklyGoal =
      goals.filter((g) => g.effectiveWeek <= summary.weekStart).at(-1)?.days ??
      summary.weeklyGoal;
    const wasMet = d.summary.workoutDaysThisWeek >= d.summary.weeklyGoal;
    const met = summary.workoutDaysThisWeek >= summary.weeklyGoal;
    summary.currentStreak = Math.max(
      0,
      summary.currentStreak + (met ? 1 : 0) - (wasMet ? 1 : 0),
    );
    summary.longestStreak = Math.max(
      summary.longestStreak,
      summary.currentStreak,
    );
    summary.weeks = summary.weeks.map((w) =>
      w.start === summary.weekStart
        ? { ...w, days: summary.workoutDaysThisWeek, met }
        : w,
    );
    return { ...d, summary };
  });
}
export function rollWeek(dashboard: Dashboard[], today: string): Dashboard[] {
  const date = new Date(`${today}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() - ((date.getUTCDay() + 6) % 7));
  const week = date.toISOString().slice(0, 10);
  return dashboard.map((d) => {
    if (d.summary.weekStart >= week) return d;
    const old = d.summary;
    const next = new Date(`${old.weekStart}T12:00:00Z`);
    next.setUTCDate(next.getUTCDate() + 7);
    const consecutiveWeek = next.toISOString().slice(0, 10) === week;
    const goal = consecutiveWeek
      ? (old.nextWeeklyGoal ?? old.weeklyGoal)
      : old.weeklyGoal;
    const calendar = Array.from({ length: 7 }, (_, i) => {
      const day = new Date(date);
      day.setUTCDate(day.getUTCDate() + i);
      return { date: day.toISOString().slice(0, 10), workedOut: false };
    });
    return {
      ...d,
      summary: {
        ...old,
        weekStart: week,
        calendar,
        workoutDaysThisWeek: 0,
        weeklyGoal: goal,
        nextWeeklyGoal: null,
        currentStreak:
          consecutiveWeek && old.workoutDaysThisWeek >= old.weeklyGoal
            ? old.currentStreak
            : 0,
        weeks: [...old.weeks, { start: week, days: 0, goal, met: false }].slice(
          -12,
        ),
      },
    };
  });
}
export async function lastRefresh(): Promise<number | undefined> {
  const db = await database();
  return (await db.get("cache", `${account}:/dashboard`))?.at;
}
export async function syncError(): Promise<string> {
  const db = await database();
  return (await db.get("meta", "syncError")) ?? "";
}
