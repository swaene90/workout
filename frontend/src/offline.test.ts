import { beforeEach, afterEach, expect, test, vi } from "vitest";
import { request, ApiError } from "./api";
import { offlineRequest, pending, sync, clearLocal, resolve } from "./offline";
import type { Me, WorkoutInput, Workout, History } from "./types";
vi.mock("./api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./api")>()),
  request: vi.fn(),
}));
const user = "e1111111-1111-4111-8111-111111111111";
const me: Me = {
  user: { id: user, name: "You", email: "you@example.com" },
  csrfToken: "fresh",
  today: "2026-10-02",
  timeZone: "America/New_York",
};
const input: WorkoutInput = {
  date: "2026-10-02",
  title: "Offline strength",
  type: "Strength",
  completed: true,
  durationMinutes: 30,
  notes: "Local note",
  exercises: [],
};
let online = true;
let server = new Map<string, Workout>();
let receipt = new Map<string, Workout | { deleted: boolean }>();
beforeEach(async () => {
  online = true;
  vi.spyOn(navigator, "onLine", "get").mockImplementation(() => online);
  await clearLocal();
  server = new Map();
  receipt = new Map();
  vi.mocked(request).mockReset();
  vi.mocked(request).mockImplementation(async (path, _method, body) => {
    if (!online) throw new TypeError("offline");
    if (path === "/me") return structuredClone(me) as never;
    if (path.startsWith("/workouts?"))
      return {
        items: [...server.values()],
        total: server.size,
        page: 1,
      } as never;
    if (path.startsWith("/workouts/")) {
      const w = server.get(path.split("/")[2]);
      if (!w) throw new ApiError(404, "Missing");
      return structuredClone(w) as never;
    }
    if (path === "/workout-mutations") {
      const p = body as {
        mutationId: string;
        workoutId: string;
        operation: string;
        expectedRevision: number | null;
        workout: WorkoutInput;
      };
      if (receipt.has(p.mutationId))
        return structuredClone(receipt.get(p.mutationId)) as never;
      const old = server.get(p.workoutId);
      if (
        p.operation !== "create" &&
        (!old || old.revision !== p.expectedRevision)
      )
        throw new ApiError(409, "Changed on another device", {
          current: old ?? null,
        });
      const w = {
        ...p.workout,
        id: p.workoutId,
        userId: user,
        revision: (old?.revision ?? 0) + 1,
      };
      if (p.operation === "delete") server.delete(p.workoutId);
      else server.set(p.workoutId, w);
      const result = p.operation === "delete" ? { deleted: true } : w;
      receipt.set(p.mutationId, result);
      return structuredClone(result) as never;
    }
    return [] as never;
  });
  await offlineRequest("/me", "GET");
});
afterEach(async () => {
  online = true;
  await clearLocal();
  vi.restoreAllMocks();
});
test("offline create, edit, and discard compact without server writes", async () => {
  online = false;
  const w = await offlineRequest<Workout>("/workouts", "POST", input);
  await offlineRequest(`/workouts/${w.id}`, "PUT", {
    ...input,
    title: "Changed before sync",
  });
  expect(await pending()).toHaveLength(1);
  expect((await pending())[0].operation).toBe("create");
  expect((await pending())[0].workout?.title).toBe("Changed before sync");
  await offlineRequest(`/workouts/${w.id}`, "DELETE");
  expect(await pending()).toHaveLength(0);
  expect(server.size).toBe(0);
});
test("a lost response retries the identical frozen mutation without duplicate workouts", async () => {
  online = false;
  await offlineRequest("/workouts", "POST", input);
  online = true;
  const handler = vi.mocked(request).getMockImplementation()!;
  let lost = true;
  vi.mocked(request).mockImplementation(async (path, method, body) => {
    const result = await handler(path, method, body);
    if (path === "/workout-mutations" && lost) {
      lost = false;
      throw new TypeError("Response lost after commit");
    }
    return result;
  });
  await sync();
  const frozen = (await pending())[0];
  expect(frozen.frozen).toBe(true);
  expect(server.size).toBe(1);
  await sync();
  expect(await pending()).toHaveLength(0);
  expect(server.size).toBe(1);
  const sent = vi
    .mocked(request)
    .mock.calls.filter((c) => c[0] === "/workout-mutations")
    .map((c) => c[2]);
  expect(sent[0]).toEqual(sent[1]);
});
test("editing after a lost create response sends the create receipt then the latest update", async () => {
  online = false;
  const w = await offlineRequest<Workout>("/workouts", "POST", input);
  online = true;
  const handler = vi.mocked(request).getMockImplementation()!;
  let lost = true;
  vi.mocked(request).mockImplementation(async (path, method, body) => {
    const result = await handler(path, method, body);
    if (path === "/workout-mutations" && lost) {
      lost = false;
      throw new TypeError("lost");
    }
    return result;
  });
  await sync();
  online = false;
  await offlineRequest(`/workouts/${w.id}`, "PUT", {
    ...input,
    title: "Latest edit",
  });
  expect(await pending()).toHaveLength(2);
  online = true;
  await sync();
  expect(await pending()).toHaveLength(0);
  expect(server.get(w.id)?.title).toBe("Latest edit");
  expect(server.get(w.id)?.revision).toBe(2);
});
test("offline edits and deletes preserve expected revisions and survive reopening", async () => {
  const id = crypto.randomUUID();
  server.set(id, { ...input, id, userId: user, revision: 4 });
  await offlineRequest(`/workouts/${id}`, "GET");
  await offlineRequest("/workouts?page=1", "GET");
  online = false;
  await offlineRequest(`/workouts/${id}`, "PUT", {
    ...input,
    title: "Queued edit",
  });
  const profile = await offlineRequest<Me>("/me", "GET");
  expect(profile.csrfToken).toBe("");
  expect((await pending())[0].expectedRevision).toBe(4);
  expect(
    (await offlineRequest<History>("/workouts?page=1", "GET")).items[0].title,
  ).toBe("Queued edit");
  await offlineRequest(`/workouts/${id}`, "DELETE");
  expect((await pending())[0].operation).toBe("delete");
  online = true;
  await sync();
  expect(server.has(id)).toBe(false);
  expect(await pending()).toHaveLength(0);
});
test("conflicting updates retain local content until explicitly resolved", async () => {
  const id = crypto.randomUUID();
  server.set(id, { ...input, id, userId: user, revision: 1 });
  await offlineRequest(`/workouts/${id}`, "GET");
  online = false;
  await offlineRequest(`/workouts/${id}`, "PUT", {
    ...input,
    notes: "Keep my note",
  });
  server.set(id, {
    ...input,
    id,
    userId: user,
    revision: 2,
    notes: "Other device",
  });
  online = true;
  await sync();
  const p = (await pending())[0];
  expect(p.conflict?.notes).toBe("Other device");
  expect(p.workout?.notes).toBe("Keep my note");
  await resolve(p, true);
  expect(await pending()).toHaveLength(0);
  expect(server.get(id)?.notes).toBe("Keep my note");
  expect(server.get(id)?.revision).toBe(3);
});
test("expired authentication never discards queued changes", async () => {
  online = false;
  await offlineRequest("/workouts", "POST", input);
  online = true;
  vi.mocked(request).mockRejectedValue(new ApiError(401, "Sign in"));
  await sync();
  expect(await pending()).toHaveLength(1);
  expect(server.size).toBe(0);
});
test("a different account cannot replay another member's queued workouts", async () => {
  online = false;
  await offlineRequest("/workouts", "POST", input);
  online = true;
  vi.mocked(request).mockResolvedValue({
    ...me,
    user: { ...me.user, id: "other" },
  } as never);
  await sync();
  expect(await pending()).toHaveLength(1);
  expect(server.size).toBe(0);
  await expect(offlineRequest("/me", "GET")).rejects.toThrow(
    "previous account",
  );
});

test("an open editor keeps its original revision even after a background cache refresh", async () => {
  const id = crypto.randomUUID();
  server.set(id, { ...input, id, userId: user, revision: 1 });
  await offlineRequest(`/workouts/${id}`, "GET");
  server.set(id, {
    ...input,
    id,
    userId: user,
    revision: 2,
    notes: "Newer server change",
  });
  await offlineRequest(`/workouts/${id}`, "GET");
  await offlineRequest(`/workouts/${id}`, "PUT", {
    ...input,
    notes: "Editor based on revision one",
    expectedRevision: 1,
  });
  const queued = (await pending())[0];
  expect(queued.error).toBeTruthy();
  expect(queued.expectedRevision).toBe(1);
  expect(queued.conflict?.revision).toBe(2);
  expect(server.get(id)?.notes).toBe("Newer server change");
});

test("keeping the server version discards only that workout's pending changes", async () => {
  const id = crypto.randomUUID();
  server.set(id, { ...input, id, userId: user, revision: 1 });
  await offlineRequest(`/workouts/${id}`, "GET");
  online = false;
  await offlineRequest(`/workouts/${id}`, "PUT", {
    ...input,
    notes: "Conflicting note",
  });
  await offlineRequest("/workouts", "POST", {
    ...input,
    title: "Another workout",
  });
  server.set(id, {
    ...input,
    id,
    userId: user,
    revision: 2,
    notes: "Keep server note",
  });
  online = true;
  await sync();
  const conflict = (await pending())[0];
  await resolve(conflict, false);
  expect(await pending()).toHaveLength(0);
  expect(server.get(id)?.notes).toBe("Keep server note");
  expect([...server.values()].some((w) => w.title === "Another workout")).toBe(
    true,
  );
});

test("an offline Monday advances the calendar and closes an unfinished previous week", async () => {
  const { rollWeek } = await import("./offline");
  const row = {
    user: me.user,
    summary: {
      currentStreak: 4,
      longestStreak: 5,
      workoutDaysThisWeek: 2,
      weeklyGoal: 3,
      nextWeeklyGoal: 2,
      weekStart: "2026-09-28",
      calendar: [],
      weeks: [{ start: "2026-09-28", days: 2, goal: 3, met: false }],
    },
  };
  const [rolled] = rollWeek([row], "2026-10-05");
  expect(rolled.summary.weekStart).toBe("2026-10-05");
  expect(rolled.summary.calendar[6].date).toBe("2026-10-11");
  expect(rolled.summary.weeklyGoal).toBe(2);
  expect(rolled.summary.currentStreak).toBe(0);
  row.summary.workoutDaysThisWeek = 3;
  row.summary.currentStreak = 5;
  expect(rollWeek([row], "2026-10-05")[0].summary.currentStreak).toBe(5);
});
