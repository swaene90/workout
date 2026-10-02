import { useCallback, useEffect, useState } from "react";
import {
  Activity,
  ArrowRight,
  CalendarDays,
  Check,
  ChevronLeft,
  ChevronRight,
  Dumbbell,
  Flame,
  LayoutDashboard,
  LogOut,
  Plus,
  Settings,
  Trophy,
  TrendingUp,
  Copy,
  Pencil,
  Trash2,
  X,
  Footprints,
} from "lucide-react";
import { api, ApiError, demoMode, setCsrf } from "./api";
import type {
  Dashboard,
  Goal,
  History,
  Me,
  ProgressPoint,
  Template,
  Workout,
  WorkoutInput,
} from "./types";
import WorkoutEditor from "./WorkoutEditor";
import type { EditorState } from "./WorkoutEditor";
import AppearanceSettings from "./AppearanceSettings";
import { applyAppearance, isAppearance, readAppearance } from "./appearance";

type Page = "dashboard" | "history" | "templates" | "progress" | "settings";
const nav = [
  { id: "dashboard", label: "Overview", icon: LayoutDashboard },
  { id: "history", label: "Workouts", icon: Dumbbell },
  { id: "templates", label: "Templates", icon: Copy },
  { id: "progress", label: "Progress", icon: TrendingUp },
  { id: "settings", label: "Settings", icon: Settings },
] as const;
const dateLabel = (
  date: string,
  options: Intl.DateTimeFormatOptions = { month: "short", day: "numeric" },
) =>
  new Date(`${date}T12:00:00Z`).toLocaleDateString("en-US", {
    ...options,
    timeZone: "UTC",
  });
const initialWorkout = (today: string): WorkoutInput => ({
  date: today,
  title: "Daily check-in",
  type: "Other",
  completed: true,
  durationMinutes: null,
  notes: "",
  exercises: [],
});

export default function App() {
  const [appearance, setAppearance] = useState(readAppearance);
  const [me, setMe] = useState<Me | null>(null),
    [loading, setLoading] = useState(true),
    [signedOut, setSignedOut] = useState(false);
  const [dashboard, setDashboard] = useState<Dashboard[]>([]),
    [history, setHistory] = useState<History>({ items: [], total: 0, page: 1 });
  const [templates, setTemplates] = useState<Template[]>([]),
    [goals, setGoals] = useState<Goal[]>([]);
  const [page, setPage] = useState<Page>("dashboard"),
    [editor, setEditor] = useState<EditorState | null>(null),
    [detail, setDetail] = useState<Workout | null>(null);
  const [error, setError] = useState(""),
    [toast, setToast] = useState(""),
    [busy, setBusy] = useState(false);
  const [filterUser, setFilterUser] = useState(""),
    [from, setFrom] = useState(""),
    [to, setTo] = useState(""),
    [historyPage, setHistoryPage] = useState(1);
  const [progressUser, setProgressUser] = useState(""),
    [exercise, setExercise] = useState(""),
    [progress, setProgress] = useState<ProgressPoint[]>([]);
  const [goalDays, setGoalDays] = useState(3),
    [deleteTarget, setDeleteTarget] = useState<{
      kind: "workouts" | "templates";
      id: string;
    } | null>(null);
  const historyPath = `/workouts?${new URLSearchParams({ page: String(historyPage), ...(filterUser ? { userId: filterUser } : {}), ...(from ? { from } : {}), ...(to ? { to } : {}) })}`;
  const reload = useCallback(async () => {
    const [d, h, t, g] = await Promise.all([
      api<Dashboard[]>("/dashboard"),
      api<History>(historyPath),
      api<Template[]>("/templates"),
      api<Goal[]>("/goals"),
    ]);
    setDashboard(d);
    setHistory(h);
    setTemplates(t);
    setGoals(g);
  }, [historyPath]);
  useEffect(() => {
    let cancelled = false;
    api<Me>("/me")
      .then(async (user) => {
        if (cancelled) return;
        setCsrf(user.csrfToken);
        setMe(user);
        const savedAppearance = {
          theme: user.user.theme,
          mode: user.user.mode,
        };
        if (isAppearance(savedAppearance)) {
          setAppearance(savedAppearance);
          applyAppearance(savedAppearance);
        }
        setProgressUser(user.user.id);
        await reload();
      })
      .catch((e) => {
        if (!cancelled) {
          if (e instanceof ApiError && e.status === 401) setSignedOut(true);
          else setError(e.message);
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []); // Authentication is loaded once; filters have their own effect below.
  useEffect(() => {
    if (me)
      api<History>(historyPath)
        .then(setHistory)
        .catch((e) => setError(e.message));
  }, [historyPath, me]);
  useEffect(() => {
    let cancelled = false;
    if (page !== "progress" || !progressUser || !exercise.trim()) {
      setProgress([]);
      return;
    }
    const timeout = setTimeout(
      () =>
        api<ProgressPoint[]>(
          `/progress?${new URLSearchParams({ userId: progressUser, exercise: exercise.trim() })}`,
        )
          .then((p) => {
            if (!cancelled) setProgress(p);
          })
          .catch((e) => {
            if (!cancelled) setError(e.message);
          }),
      200,
    );
    return () => {
      cancelled = true;
      clearTimeout(timeout);
    };
  }, [page, progressUser, exercise]);
  const own = dashboard.find((d) => d.user.id === me?.user.id);
  useEffect(() => {
    if (own) setGoalDays(own.summary.nextWeeklyGoal ?? own.summary.weeklyGoal);
  }, [own?.summary.nextWeeklyGoal, own?.summary.weeklyGoal]);
  useEffect(() => {
    if (toast) {
      const timer = setTimeout(() => setToast(""), 4500);
      return () => clearTimeout(timer);
    }
  }, [toast]);
  const action = async (operation: () => Promise<unknown>, message: string) => {
    setBusy(true);
    setError("");
    try {
      await operation();
      await reload();
      setToast(message);
      return true;
    } catch (e) {
      setError((e as Error).message);
      return false;
    } finally {
      setBusy(false);
    }
  };
  const navigate = (p: Page) => {
    setPage(p);
    setEditor(null);
    setDetail(null);
    setError("");
  };
  const completedToday = own?.summary.calendar.some(
    (d) => d.date === me?.today && d.workedOut,
  );
  const changeFilter = (setter: (value: string) => void, value: string) => {
    setter(value);
    setHistoryPage(1);
  };

  if (loading)
    return (
      <div className="loading">
        <span className="brand-mark">
          <Activity />
        </span>
        <p>Getting your week ready…</p>
      </div>
    );
  if (!me || signedOut)
    return (
      <main className="login">
        <div className="login-card">
          <div className="brand">
            <span className="brand-mark">
              <Activity size={23} />
            </span>
            <span>
              workout<span className="brand-dot">.</span>
            </span>
          </div>
          <p className="eyebrow">BETTER, TOGETHER</p>
          <h1>
            A little consistency.
            <br />A lot of progress.
          </h1>
          <p>
            Keep showing up. Track your workouts, build your weekly streak, and
            bring your brother along.
          </p>
          <a className="button primary" href="/auth/login">
            Continue with Google <ArrowRight size={18} />
          </a>
          <p className="login-note">A private space for you and Britt.</p>
          {(error || new URLSearchParams(location.search).has("authError")) && (
            <p className="error" role="alert">
              {error ||
                "Sign-in failed. Use one of the two allowed Google accounts and try again."}
            </p>
          )}
        </div>
      </main>
    );

  return (
    <div className="app-shell">
      {demoMode && (
        <div className="demo-banner">
          Sample-data preview · workout changes reset on refresh
        </div>
      )}
      <header className="app-header">
        <div className="header-inner">
          <button className="brand" onClick={() => navigate("dashboard")}>
            <span className="brand-mark">
              <Activity size={22} />
            </span>
            <span>
              workout<span className="brand-dot">.</span>
            </span>
          </button>
          <nav aria-label="Main navigation">
            {nav.map((n) => (
              <button
                key={n.id}
                className={page === n.id && !editor ? "active" : ""}
                onClick={() => navigate(n.id)}
              >
                <n.icon size={17} />
                <span>{n.label}</span>
              </button>
            ))}
          </nav>
          <div className="header-user">
            <span className="avatar small">{me.user.name[0]}</span>
            <span>{me.user.name}</span>
          </div>
        </div>
      </header>
      <main className="main-content">
        {error && (
          <div className="error global-error" role="alert">
            {error}
            <button
              className="icon-button"
              aria-label="Dismiss error"
              onClick={() => setError("")}
            >
              <X size={16} />
            </button>
          </div>
        )}
        {editor ? (
          <WorkoutEditor
            key={JSON.stringify(editor)}
            editor={editor}
            today={me.today}
            onCancel={() => setEditor(null)}
            onSaved={() => {
              setEditor(null);
              void action(async () => {}, "Saved. Keep showing up!");
            }}
          />
        ) : detail ? (
          <section>
            <button
              className="text-button back"
              onClick={() => setDetail(null)}
            >
              <ChevronLeft size={17} /> Back to workouts
            </button>
            <div className="page-title">
              <div>
                <p className="eyebrow">
                  {
                    dashboard.find((d) => d.user.id === detail.userId)?.user
                      .name
                  }{" "}
                  · {dateLabel(detail.date)}
                </p>
                <h1>{detail.title}</h1>
              </div>
              {detail.userId === me.user.id && (
                <button
                  className="button primary"
                  onClick={() => {
                    setEditor({ workout: detail });
                    setDetail(null);
                  }}
                >
                  <Pencil size={16} /> Edit workout
                </button>
              )}
            </div>
            <div className="panel detail-panel">
              <div className="button-row">
                <span className="tag">{detail.type}</span>
                <span className={`tag ${detail.completed ? "success" : ""}`}>
                  {detail.completed ? "Completed" : "Draft · not counted"}
                </span>
                {detail.durationMinutes && (
                  <span className="tag">{detail.durationMinutes} min</span>
                )}
              </div>
              {detail.exercises.length === 0 && (
                <p className="muted">
                  A simple check-in. Showing up is what matters.
                </p>
              )}
              {detail.exercises.map((e, i) => (
                <div className="detail-exercise" key={i}>
                  <h2>{e.name}</h2>
                  {e.kind === "Cardio" ? (
                    <p>
                      {e.durationMinutes} minutes
                      {e.distanceMiles !== null &&
                        ` · ${e.distanceMiles} miles`}
                    </p>
                  ) : (
                    <div className="table-scroll">
                      <table>
                        <thead>
                          <tr>
                            <th>Set</th>
                            <th>Reps</th>
                            <th>Weight</th>
                          </tr>
                        </thead>
                        <tbody>
                          {e.sets.map((s, n) => (
                            <tr key={n}>
                              <td>{n + 1}</td>
                              <td>{s.reps}</td>
                              <td>{s.weightLb} lb</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              ))}
              {detail.notes && <p className="workout-notes">{detail.notes}</p>}
            </div>
          </section>
        ) : page === "dashboard" ? (
          <>
            <div className="page-title">
              <div>
                <p className="eyebrow">YOUR WEEK, ONE WORKOUT AT A TIME</p>
                <h1>
                  Keep showing up<span className="green">.</span>
                </h1>
                <p className="subtitle">
                  Small steps. Stronger habits. A little accountability.
                </p>
              </div>
              <span className="date-pill">
                <CalendarDays size={15} />{" "}
                {dateLabel(me.today, {
                  weekday: "short",
                  month: "short",
                  day: "numeric",
                })}
              </span>
            </div>
            <div className="checkin-banner">
              <div className="checkin-icon">
                {completedToday ? <Check /> : <Dumbbell />}
              </div>
              <div className="grow">
                <h2>
                  {completedToday
                    ? "You showed up today."
                    : "Got a workout in today?"}
                </h2>
                <p>
                  {completedToday
                    ? "Another day toward your goal. Nice work."
                    : "One tap is all it takes to keep the momentum going."}
                </p>
              </div>
              <button
                className="button lime"
                disabled={busy || completedToday}
                onClick={() =>
                  void action(
                    () => api("/workouts", "POST", initialWorkout(me.today)),
                    "Today is checked in!",
                  )
                }
              >
                {completedToday ? <Check size={17} /> : <Plus size={17} />}
                {busy
                  ? "Checking in…"
                  : completedToday
                    ? "Checked in"
                    : "Check in today"}
              </button>
            </div>
            <div className="section-heading">
              <h2>Your streaks</h2>
              <span className="muted">
                {own &&
                  `${dateLabel(own.summary.weekStart)} – ${dateLabel(own.summary.calendar[6].date)}`}
              </span>
            </div>
            <div className="streak-grid">
              {[...dashboard]
                .sort(
                  (a, b) =>
                    Number(b.user.id === me.user.id) -
                    Number(a.user.id === me.user.id),
                )
                .map((d) => (
                  <article
                    key={d.user.id}
                    className={`streak-card ${d.user.id === me.user.id ? "mine" : ""}`}
                  >
                    <div className="card-person">
                      <span
                        className={`avatar ${d.user.id === me.user.id ? "you" : "britt"}`}
                      >
                        {d.user.name[0]}
                      </span>
                      <div>
                        <h3>{d.user.name}</h3>
                        <p>
                          {d.user.id === me.user.id
                            ? "Your consistency is paying off"
                            : "Your accountability partner"}
                        </p>
                      </div>
                      <span className="streak-symbol">
                        <Flame size={21} />
                      </span>
                    </div>
                    <div className="streak-number">
                      <span>{d.summary.currentStreak}</span>
                      <div>
                        <strong>week streak</strong>
                        <span>
                          {d.summary.currentStreak
                            ? "Let’s keep it going"
                            : "A fresh week to start"}
                        </span>
                      </div>
                    </div>
                    <div className="goal-row">
                      <span>This week</span>
                      <strong>
                        {d.summary.workoutDaysThisWeek}
                        <span> / {d.summary.weeklyGoal} days</span>
                      </strong>
                    </div>
                    <div className="progress-track">
                      <div
                        style={{
                          width: `${Math.min(100, (d.summary.workoutDaysThisWeek / d.summary.weeklyGoal) * 100)}%`,
                        }}
                      />
                    </div>
                    <div className="week-calendar">
                      {d.summary.calendar.map((day, i) => (
                        <div
                          key={day.date}
                          className={day.date === me.today ? "today" : ""}
                        >
                          <span>{["M", "T", "W", "T", "F", "S", "S"][i]}</span>
                          <div
                            className={`day ${day.workedOut ? "done" : ""} ${day.date > me.today ? "future" : ""}`}
                            title={`${dateLabel(day.date)}: ${day.workedOut ? "worked out" : "no workout"}`}
                          >
                            {day.workedOut ? (
                              <Check size={15} strokeWidth={3} />
                            ) : (
                              dateLabel(day.date, { day: "numeric" })
                            )}
                          </div>
                        </div>
                      ))}
                    </div>
                    <div className="card-footer">
                      <span>
                        <Trophy size={14} /> Best:{" "}
                        <strong>{d.summary.longestStreak} weeks</strong>
                      </span>
                      <span
                        className={
                          d.summary.workoutDaysThisWeek >= d.summary.weeklyGoal
                            ? "goal-met"
                            : ""
                        }
                      >
                        {d.summary.workoutDaysThisWeek >= d.summary.weeklyGoal
                          ? "Goal complete ✓"
                          : `${d.summary.weeklyGoal - d.summary.workoutDaysThisWeek} more to your goal`}
                      </span>
                    </div>
                  </article>
                ))}
            </div>
            <div className="dashboard-lower">
              <section className="panel recent-panel">
                <div className="section-heading">
                  <h2>Recent workouts</h2>
                  <button
                    className="text-button"
                    onClick={() => navigate("history")}
                  >
                    View all <ArrowRight size={14} />
                  </button>
                </div>
                {history.items.slice(0, 4).map((w) => (
                  <button
                    className="recent-row"
                    key={w.id}
                    onClick={() => setDetail(w)}
                  >
                    <span className="workout-icon">
                      {w.type === "Cardio" ? (
                        <Footprints size={18} />
                      ) : (
                        <Dumbbell size={18} />
                      )}
                    </span>
                    <span className="grow">
                      <strong>{w.title}</strong>
                      <small>
                        {
                          dashboard.find((d) => d.user.id === w.userId)?.user
                            .name
                        }{" "}
                        · {dateLabel(w.date)}
                        {w.durationMinutes && ` · ${w.durationMinutes} min`}
                      </small>
                    </span>
                    <span className="tag">
                      {w.completed ? w.type : "Draft"}
                    </span>
                    <ChevronRight size={16} />
                  </button>
                ))}
                {history.items.length === 0 && (
                  <p className="empty-inline">
                    Your first workout starts the story. Check in above.
                  </p>
                )}
              </section>
              <aside className="habit-note">
                <Flame size={25} />
                <p className="eyebrow">CONSISTENCY OVER PERFECTION</p>
                <h2>
                  You don’t have to go big.
                  <br />
                  Just keep going.
                </h2>
                <p>
                  Hit your weekly goal to grow your streak. Rest days are part
                  of the plan.
                </p>
                <button className="text-button" onClick={() => setEditor({})}>
                  Log a full workout <ArrowRight size={15} />
                </button>
              </aside>
            </div>
            <section className="panel weekly-history">
              <div className="section-heading">
                <h2>The bigger picture</h2>
                <span className="muted">Last 12 weeks</span>
              </div>
              {dashboard.map((d) => (
                <div className="week-history-row" key={d.user.id}>
                  <strong>{d.user.name}</strong>
                  <div>
                    {d.summary.weeks.map((w) => (
                      <span
                        key={w.start}
                        className={`week-block ${w.met ? "met" : ""}`}
                        title={`Week of ${dateLabel(w.start)}: ${w.days}/${w.goal} days`}
                      >
                        {w.met ? <Check size={13} /> : w.days}
                      </span>
                    ))}
                  </div>
                </div>
              ))}
              <p className="muted small-text">
                Weeks start Monday. An unfinished current week preserves your
                streak; missed past weeks break it.
              </p>
            </section>
          </>
        ) : page === "history" ? (
          <>
            <div className="page-title">
              <div>
                <p className="eyebrow">THE WORK YOU PUT IN</p>
                <h1>Workout history</h1>
                <p className="subtitle">Your sessions, side by side.</p>
              </div>
              <button className="button primary" onClick={() => setEditor({})}>
                <Plus size={17} /> Log workout
              </button>
            </div>
            <div className="panel">
              <div className="filters">
                <label>
                  Person
                  <select
                    value={filterUser}
                    onChange={(e) =>
                      changeFilter(setFilterUser, e.target.value)
                    }
                  >
                    <option value="">Both of us</option>
                    {dashboard.map((d) => (
                      <option key={d.user.id} value={d.user.id}>
                        {d.user.name}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  From
                  <input
                    type="date"
                    value={from}
                    onChange={(e) => changeFilter(setFrom, e.target.value)}
                  />
                </label>
                <label>
                  To
                  <input
                    type="date"
                    value={to}
                    onChange={(e) => changeFilter(setTo, e.target.value)}
                  />
                </label>
              </div>
              <div className="table-scroll desktop-history">
                <table>
                  <thead>
                    <tr>
                      <th>Workout</th>
                      <th>Person</th>
                      <th>Date</th>
                      <th>Status</th>
                      <th>Duration</th>
                      <th>
                        <span className="sr-only">Actions</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {history.items.map((w) => (
                      <tr key={w.id}>
                        <td>
                          <button
                            className="table-link"
                            onClick={() => setDetail(w)}
                          >
                            {w.title}
                          </button>
                          <small>
                            {w.type} · {w.exercises.length} exercises
                          </small>
                        </td>
                        <td>
                          {
                            dashboard.find((d) => d.user.id === w.userId)?.user
                              .name
                          }
                        </td>
                        <td>
                          {dateLabel(w.date, {
                            month: "short",
                            day: "numeric",
                            year: "numeric",
                          })}
                        </td>
                        <td>
                          <span
                            className={`tag ${w.completed ? "success" : ""}`}
                          >
                            {w.completed ? "Completed" : "Draft"}
                          </span>
                        </td>
                        <td>
                          {w.durationMinutes ? `${w.durationMinutes} min` : "—"}
                        </td>
                        <td>
                          {w.userId === me.user.id && (
                            <div className="button-row">
                              <button
                                className="icon-button"
                                aria-label={`Edit ${w.title}`}
                                onClick={() => setEditor({ workout: w })}
                              >
                                <Pencil size={16} />
                              </button>
                              <button
                                className="icon-button danger"
                                aria-label={`Delete ${w.title}`}
                                onClick={() =>
                                  setDeleteTarget({
                                    kind: "workouts",
                                    id: w.id,
                                  })
                                }
                              >
                                <Trash2 size={16} />
                              </button>
                            </div>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <div className="mobile-history">
                {history.items.map((w) => (
                  <article key={w.id}>
                    <button
                      className="mobile-workout-main"
                      onClick={() => setDetail(w)}
                    >
                      <span className="workout-icon">
                        {w.type === "Cardio" ? (
                          <Footprints size={19} />
                        ) : (
                          <Dumbbell size={19} />
                        )}
                      </span>
                      <span className="grow">
                        <strong>{w.title}</strong>
                        <small>
                          {
                            dashboard.find((d) => d.user.id === w.userId)?.user
                              .name
                          }{" "}
                          · {dateLabel(w.date)}
                          {w.durationMinutes
                            ? ` · ${w.durationMinutes} min`
                            : ""}
                        </small>
                      </span>
                      <ChevronRight size={17} />
                    </button>
                    <div className="mobile-workout-footer">
                      <span className={`tag ${w.completed ? "success" : ""}`}>
                        {w.completed ? w.type : "Draft · not counted"}
                      </span>
                      {w.userId === me.user.id && (
                        <div className="button-row">
                          <button
                            className="icon-button"
                            aria-label={`Edit ${w.title}`}
                            onClick={() => setEditor({ workout: w })}
                          >
                            <Pencil size={16} />
                          </button>
                          <button
                            className="icon-button danger"
                            aria-label={`Delete ${w.title}`}
                            onClick={() =>
                              setDeleteTarget({ kind: "workouts", id: w.id })
                            }
                          >
                            <Trash2 size={16} />
                          </button>
                        </div>
                      )}
                    </div>
                  </article>
                ))}
              </div>
              {history.items.length === 0 && (
                <div className="empty-state">
                  <Dumbbell />
                  <h2>No workouts yet</h2>
                  <p>Log your first session or adjust the filters.</p>
                </div>
              )}
              <div className="pagination">
                <span>{history.total} sessions</span>
                <div>
                  <button
                    className="icon-button"
                    aria-label="Previous page"
                    disabled={historyPage === 1}
                    onClick={() => setHistoryPage((p) => p - 1)}
                  >
                    <ChevronLeft size={18} />
                  </button>
                  <span>Page {historyPage}</span>
                  <button
                    className="icon-button"
                    aria-label="Next page"
                    disabled={historyPage * 30 >= history.total}
                    onClick={() => setHistoryPage((p) => p + 1)}
                  >
                    <ChevronRight size={18} />
                  </button>
                </div>
              </div>
            </div>
          </>
        ) : page === "templates" ? (
          <>
            <div className="page-title">
              <div>
                <p className="eyebrow">LESS SETUP, MORE SHOWING UP</p>
                <h1>Your templates</h1>
                <p className="subtitle">
                  Save your routine. Make it repeatable.
                </p>
              </div>
              <button
                className="button primary"
                onClick={() => setEditor({ templateMode: true })}
              >
                <Plus size={17} /> New template
              </button>
            </div>
            <div className="template-grid">
              {templates.map((t) => (
                <article className="panel template-card" key={t.id}>
                  <span className="workout-icon">
                    <Dumbbell size={22} />
                  </span>
                  <span className="tag">{t.type}</span>
                  <h2>{t.name}</h2>
                  <p>
                    {t.exercises.map((e) => e.name).join(" · ") ||
                      "A simple routine, ready when you are."}
                  </p>
                  <div className="template-actions">
                    <button
                      className="button primary"
                      onClick={() => setEditor({ fromTemplate: t })}
                    >
                      Use template <ArrowRight size={15} />
                    </button>
                    <button
                      className="icon-button"
                      aria-label={`Edit ${t.name}`}
                      onClick={() =>
                        setEditor({ template: t, templateMode: true })
                      }
                    >
                      <Pencil size={17} />
                    </button>
                    <button
                      className="icon-button danger"
                      aria-label={`Delete ${t.name}`}
                      onClick={() =>
                        setDeleteTarget({ kind: "templates", id: t.id })
                      }
                    >
                      <Trash2 size={17} />
                    </button>
                  </div>
                </article>
              ))}
            </div>
            {templates.length === 0 && (
              <div className="panel empty-state">
                <Copy />
                <h2>Your routine belongs here</h2>
                <p>Create a template with your go-to exercises.</p>
              </div>
            )}
          </>
        ) : page === "progress" ? (
          <>
            <div className="page-title">
              <div>
                <p className="eyebrow">PROGRESS YOU CAN SEE</p>
                <h1>A little stronger</h1>
                <p className="subtitle">
                  Look back at an exercise to see how far you’ve come.
                </p>
              </div>
            </div>
            <div className="panel">
              <div className="filters">
                <label>
                  Person
                  <select
                    value={progressUser}
                    onChange={(e) => setProgressUser(e.target.value)}
                  >
                    {dashboard.map((d) => (
                      <option key={d.user.id} value={d.user.id}>
                        {d.user.name}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="grow">
                  Exercise name
                  <input
                    placeholder="e.g. Bench press or Running"
                    value={exercise}
                    onChange={(e) => setExercise(e.target.value)}
                  />
                </label>
              </div>
              {progress.length > 0 ? (
                <>
                  <div className="progress-stats">
                    <div>
                      <span>Recorded sessions</span>
                      <strong>{progress.length}</strong>
                    </div>
                    <div>
                      <span>
                        {progress[0].kind === "Strength"
                          ? "Best weight"
                          : "Longest distance"}
                      </span>
                      <strong>
                        {Math.max(
                          ...progress.map((p) =>
                            p.kind === "Strength"
                              ? (p.maxWeightLb ?? 0)
                              : (p.distanceMiles ?? 0),
                          ),
                        )}
                        <small>
                          {" "}
                          {progress[0].kind === "Strength" ? "lb" : "mi"}
                        </small>
                      </strong>
                    </div>
                  </div>
                  <div
                    className="exercise-chart"
                    aria-label="Exercise progress chart"
                  >
                    {progress.slice(-20).map((p, i) => {
                      const value =
                        p.kind === "Strength"
                          ? (p.maxWeightLb ?? 0)
                          : (p.distanceMiles ?? p.durationMinutes ?? 0);
                      const max = Math.max(
                        1,
                        ...progress.map((x) =>
                          x.kind === "Strength"
                            ? (x.maxWeightLb ?? 0)
                            : (x.distanceMiles ?? x.durationMinutes ?? 0),
                        ),
                      );
                      return (
                        <div
                          key={i}
                          title={`${dateLabel(p.date)}: ${value} ${p.kind === "Strength" ? "lb" : p.distanceMiles !== null ? "mi" : "min"}`}
                        >
                          <span
                            style={{
                              height: `${Math.max(2, (value / max) * 130)}px`,
                            }}
                          />
                          <small>{dateLabel(p.date)}</small>
                        </div>
                      );
                    })}
                  </div>
                  <div className="table-scroll">
                    <table>
                      <thead>
                        <tr>
                          <th>Date</th>
                          <th>Best weight / distance</th>
                          <th>Volume / duration</th>
                        </tr>
                      </thead>
                      <tbody>
                        {progress.map((p, i) => (
                          <tr key={i}>
                            <td>
                              {dateLabel(p.date, {
                                month: "short",
                                day: "numeric",
                                year: "numeric",
                              })}
                            </td>
                            <td>
                              {p.kind === "Strength"
                                ? `${p.maxWeightLb ?? 0} lb`
                                : p.distanceMiles !== null
                                  ? `${p.distanceMiles} mi`
                                  : "—"}
                            </td>
                            <td>
                              {p.kind === "Strength"
                                ? `${p.volumeLb} lb`
                                : `${p.durationMinutes} min`}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </>
              ) : (
                <div className="empty-state">
                  <TrendingUp />
                  <h2>
                    {exercise
                      ? "No completed sessions found"
                      : "Every rep tells a story"}
                  </h2>
                  <p>
                    {exercise
                      ? "Use the exercise name from your workout logs."
                      : "Enter an exercise name to explore its history."}
                  </p>
                </div>
              )}
            </div>
          </>
        ) : (
          <>
            <div className="page-title">
              <div>
                <p className="eyebrow">MAKE CONSISTENCY YOUR OWN</p>
                <h1>Your settings</h1>
              </div>
            </div>
            <div className="settings-grid">
              <AppearanceSettings value={appearance} onChange={setAppearance} />
              <section className="panel settings-panel">
                <h2>Weekly workout goal</h2>
                <p className="muted">
                  Choose a goal you can show up for. Changes start next Monday
                  and leave your past streaks intact.
                </p>
                <form
                  onSubmit={(e) => {
                    e.preventDefault();
                    void action(
                      () => api("/goals", "PUT", { days: goalDays }),
                      "Your goal is set for next Monday.",
                    );
                  }}
                >
                  <label>
                    Workout days per week
                    <select
                      value={goalDays}
                      onChange={(e) => setGoalDays(Number(e.target.value))}
                    >
                      {[1, 2, 3, 4, 5, 6, 7].map((n) => (
                        <option key={n} value={n}>
                          {n} {n === 1 ? "day" : "days"}
                        </option>
                      ))}
                    </select>
                  </label>
                  <button className="button primary" disabled={busy}>
                    Save goal
                  </button>
                </form>
                {own?.summary.nextWeeklyGoal !== null &&
                  own?.summary.nextWeeklyGoal !== undefined && (
                    <p className="success-note">
                      Next week: {own.summary.nextWeeklyGoal} days
                    </p>
                  )}
                <h3>Goal history</h3>
                {goals.map((g) => (
                  <div className="goal-history" key={g.effectiveWeek}>
                    <span>
                      {g.effectiveWeek === "1970-01-05"
                        ? "Starting goal"
                        : `Week of ${dateLabel(g.effectiveWeek, { month: "short", day: "numeric", year: "numeric" })}`}
                    </span>
                    <strong>{g.days} days</strong>
                  </div>
                ))}
              </section>
              <section className="panel settings-panel">
                <h2>Your account</h2>
                <p>{me.user.email}</p>
                <p className="muted">
                  Workout dates use America/New_York. Weights are in pounds and
                  distances in miles.
                </p>
                <button
                  className="button secondary"
                  disabled={busy || demoMode}
                  onClick={async () => {
                    setBusy(true);
                    try {
                      await api("/logout", "POST");
                      setMe(null);
                      setSignedOut(true);
                    } catch (e) {
                      setError((e as Error).message);
                    } finally {
                      setBusy(false);
                    }
                  }}
                >
                  <LogOut size={16} /> Sign out
                </button>
              </section>
            </div>
          </>
        )}
        <footer className="app-footer">
          <span>Just you, Britt, and the work.</span>
          <span>
            One week at a time <Activity size={13} />
          </span>
        </footer>
      </main>
      {toast && (
        <div className="toast" role="status">
          <Check size={18} /> {toast}
        </div>
      )}
      {deleteTarget && (
        <div className="modal-backdrop">
          <section
            className="confirm-dialog"
            role="dialog"
            aria-modal="true"
            aria-labelledby="delete-title"
          >
            <h2 id="delete-title">
              Delete this{" "}
              {deleteTarget.kind === "workouts" ? "workout" : "template"}?
            </h2>
            <p>
              {deleteTarget.kind === "workouts"
                ? "Your streak will be recalculated. Other workouts on this date still count."
                : "Your existing workout logs will stay in place."}
            </p>
            <div className="button-row">
              <button
                className="button secondary"
                onClick={() => setDeleteTarget(null)}
              >
                Cancel
              </button>
              <button
                className="button danger-button"
                disabled={busy}
                onClick={async () => {
                  if (
                    await action(
                      () =>
                        api(
                          `/${deleteTarget.kind}/${deleteTarget.id}`,
                          "DELETE",
                        ),
                      "Deleted.",
                    )
                  )
                    setDeleteTarget(null);
                }}
              >
                Delete
              </button>
            </div>
          </section>
        </div>
      )}
    </div>
  );
}
