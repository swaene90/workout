import { vi } from "vitest";
import type {
  Dashboard,
  Goal,
  Me,
  Template,
  Workout,
  WorkoutInput,
} from "./types";

beforeEach(() => {
  vi.resetModules();
  localStorage.clear();
});
afterEach(() => {
  window.history.replaceState({}, "", "/");
  vi.unstubAllGlobals();
});

test("public demo supports all data features without calling the real API", async () => {
  window.history.replaceState({}, "", "/demo");
  const fetchSpy = vi.fn(() => {
    throw new Error("Demo must never call the server");
  });
  vi.stubGlobal("fetch", fetchSpy);
  const { api, demoMode } = await import("./api");
  expect(demoMode).toBe(true);
  const me = await api<Me>("/me");
  const dashboard = await api<Dashboard[]>("/dashboard");
  expect(dashboard).toHaveLength(2);
  expect(JSON.stringify({ me, dashboard })).not.toMatch(/Austin|Britt/i);
  expect(dashboard[0].summary.currentStreak).toBe(6);
  const history = await api<{ items: Workout[] }>("/workouts");
  expect(history.items.some((w) => !w.completed)).toBe(true);
  const templates = await api<Template[]>("/templates");
  expect(templates.map((t) => t.type)).toEqual(["Strength", "Cardio"]);
  expect(
    await api(`/progress?userId=${me.user.id}&exercise=Bench+press`),
  ).not.toEqual([]);
  expect(
    await api(`/progress?userId=${me.user.id}&exercise=Running`),
  ).not.toEqual([]);
  const input: WorkoutInput = {
    date: me.today,
    title: "Demo check-in",
    type: "Other",
    completed: true,
    durationMinutes: null,
    notes: "",
    exercises: [],
  };
  const created = await api<Workout>("/workouts", "POST", input);
  expect(created.userId).toBe(me.user.id);
  const edited = await api<Workout>(`/workouts/${created.id}`, "PUT", {
    ...input,
    title: "Edited check-in",
  });
  expect(edited.title).toBe("Edited check-in");
  await api(`/workouts/${created.id}`, "DELETE");
  const template = await api<Template>("/templates", "POST", {
    ...templates[0],
    name: "Demo template",
  });
  await api(`/templates/${template.id}`, "PUT", {
    ...template,
    name: "Edited template",
  });
  await api(`/templates/${template.id}`, "DELETE");
  const goals = await api<Goal[]>("/goals", "PUT", { days: 4 });
  expect(goals.at(-1)?.days).toBe(4);
  await api("/preferences", "PUT", { theme: "purple", mode: "dark" });
  expect(localStorage.getItem("workout.demo.appearance")).toContain("purple");
  expect(localStorage.getItem("workout.appearance")).toBeNull();
  const brotherWorkout = history.items.find((w) => w.userId !== me.user.id)!;
  await expect(api(`/workouts/${brotherWorkout.id}`, "DELETE")).rejects.toThrow(
    "own workouts",
  );
  await expect(
    api("/workouts", "POST", { ...input, date: "2999-01-01" }),
  ).rejects.toThrow("future");
  expect(fetchSpy).not.toHaveBeenCalled();
});

test("normal application paths still require the real authenticated API", async () => {
  window.history.replaceState({}, "", "/?demo=true");
  const fetchSpy = vi
    .fn()
    .mockResolvedValue({ ok: false, status: 401, json: async () => ({}) });
  vi.stubGlobal("fetch", fetchSpy);
  const { api, demoMode } = await import("./api");
  expect(demoMode).toBe(false);
  await expect(api("/me")).rejects.toMatchObject({ status: 401 });
  expect(fetchSpy).toHaveBeenCalledWith("/api/me", expect.anything());
});
