import type { Exercise, Goal, Member, Template, Workout, WorkoutInput } from './types';
const me: Member = { id: 'e1111111-1111-4111-8111-111111111111', name: 'You', email: 'swaene1@gmail.com' };
const britt: Member = { id: 'b2222222-2222-4222-8222-222222222222', name: 'Britt', email: 'swaene15@gmail.com' };
const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/New_York', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
const date = (value: string) => new Date(`${value}T12:00:00Z`);
export const addDays = (value: string, days: number) => { const d = date(value); d.setUTCDate(d.getUTCDate() + days); return d.toISOString().slice(0, 10); };
export const monday = (value: string) => addDays(value, -((date(value).getUTCDay() + 6) % 7));
const week = monday(today);
const exercise: Exercise = { name: 'Bench press', kind: 'Strength', sets: [{ reps: 8, weightLb: 135 }, { reps: 8, weightLb: 135 }], durationMinutes: null, distanceMiles: null };
let workouts: Workout[] = [me, britt].flatMap((user, u) => Array.from({ length: u ? 4 : 6 }, (_, w) => [0, 2, 4].map(d => ({
  id: crypto.randomUUID(), userId: user.id, date: addDays(week, -7 * (w + 1) + d), title: d === 2 ? 'Afternoon run' : 'Full body', type: d === 2 ? 'Cardio' : 'Strength', completed: true,
  durationMinutes: 45, notes: '', exercises: d === 2 ? [{ name: 'Running', kind: 'Cardio' as const, sets: [], durationMinutes: 30, distanceMiles: 3 }] : [{ ...exercise, sets: [{ reps: 8, weightLb: 155 - w * 5 }] }],
}))).flat());
workouts.push({ id: crypto.randomUUID(), userId: me.id, date: week, title: 'Monday momentum', type: 'Strength', completed: true, durationMinutes: 40, notes: '', exercises: [structuredClone(exercise)] });
workouts.push({ id: crypto.randomUUID(), userId: britt.id, date: week, title: 'Easy run', type: 'Cardio', completed: true, durationMinutes: 30, notes: '', exercises: [] });
let templates: Template[] = [{ id: crypto.randomUUID(), userId: me.id, name: 'Full body essentials', type: 'Strength', notes: 'Start light. Finish strong.', exercises: [structuredClone(exercise), { ...structuredClone(exercise), name: 'Squat', sets: [{ reps: 5, weightLb: 185 }] }] }];
let goals: Goal[] = [me, britt].map(u => ({ userId: u.id, effectiveWeek: '1970-01-05', days: 3 }));

export function calculateDemo(user: Member) {
  const dates = new Set(workouts.filter(w => w.userId === user.id && w.completed && w.date <= today).map(w => w.date));
  const goalAt = (w: string) => goals.filter(g => g.userId === user.id && g.effectiveWeek <= w).sort((a, b) => b.effectiveWeek.localeCompare(a.effectiveWeek))[0]?.days ?? 3;
  const first = dates.size ? monday([...dates].sort()[0]) : week;
  let currentStreak = 0, longestStreak = 0;
  const weeks = [];
  for (let w = first; w <= week; w = addDays(w, 7)) {
    const days = Array.from({ length: 7 }, (_, i) => dates.has(addDays(w, i))).filter(Boolean).length;
    const goal = goalAt(w), met = days >= goal;
    if (met) currentStreak++; else if (w < week) currentStreak = 0;
    longestStreak = Math.max(longestStreak, currentStreak);
    weeks.push({ start: w, days, goal, met });
  }
  const calendar = Array.from({ length: 7 }, (_, i) => ({ date: addDays(week, i), workedOut: dates.has(addDays(week, i)) }));
  return { user, summary: { currentStreak, longestStreak, workoutDaysThisWeek: calendar.filter(d => d.workedOut).length, weeklyGoal: goalAt(week), nextWeeklyGoal: goals.find(g => g.userId === user.id && g.effectiveWeek === addDays(week, 7))?.days ?? null, weekStart: week, calendar, weeks: weeks.slice(-12) } };
}

export async function demoRequest<T>(path: string, method: string, body: unknown): Promise<T> {
  const [route, query = ''] = path.split('?'), params = new URLSearchParams(query);
  const data = body as WorkoutInput & { name: string; days: number };
  let result: unknown;
  if (route === '/me') result = { user: me, csrfToken: 'demo', today, timeZone: 'America/New_York' };
  else if (route === '/dashboard') result = [calculateDemo(me), calculateDemo(britt)];
  else if (route === '/workouts' && method === 'GET') {
    const items = workouts.filter(w => (!params.get('userId') || w.userId === params.get('userId')) && (!params.get('from') || w.date >= params.get('from')!) && (!params.get('to') || w.date <= params.get('to')!)).sort((a, b) => b.date.localeCompare(a.date));
    const page = Number(params.get('page') || 1); result = { items: items.slice((page - 1) * 30, page * 30), total: items.length, page };
  } else if (route === '/workouts' && method === 'POST') { const w = { ...structuredClone(data), id: crypto.randomUUID(), userId: me.id }; workouts.push(w); result = w; }
  else if (route.startsWith('/workouts/')) {
    const id = route.split('/')[2];
    if (method === 'DELETE') workouts = workouts.filter(w => w.id !== id);
    else if (method === 'PUT') { workouts = workouts.map(w => w.id === id ? { ...w, ...structuredClone(data) } : w); result = workouts.find(w => w.id === id); }
    else result = workouts.find(w => w.id === id);
  } else if (route === '/templates' && method === 'GET') result = templates;
  else if (route === '/templates' && method === 'POST') { const t = { id: crypto.randomUUID(), userId: me.id, name: data.name, type: data.type, notes: data.notes, exercises: structuredClone(data.exercises) }; templates.push(t); result = t; }
  else if (route.startsWith('/templates/')) {
    const id = route.split('/')[2];
    if (method === 'DELETE') templates = templates.filter(t => t.id !== id);
    else { templates = templates.map(t => t.id === id ? { ...t, ...structuredClone(data) } : t); result = templates.find(t => t.id === id); }
  } else if (route === '/goals') {
    if (method === 'PUT') { goals = goals.filter(g => !(g.userId === me.id && g.effectiveWeek === addDays(week, 7))); goals.push({ userId: me.id, effectiveWeek: addDays(week, 7), days: data.days }); }
    result = goals.filter(g => g.userId === me.id);
  } else if (route === '/progress') result = workouts.filter(w => w.completed && w.userId === params.get('userId')).sort((a, b) => a.date.localeCompare(b.date)).flatMap(w => w.exercises.filter(e => e.name.toLowerCase() === params.get('exercise')?.toLowerCase()).map(e => ({ id: w.id, date: w.date, kind: e.kind, durationMinutes: e.durationMinutes, distanceMiles: e.distanceMiles, maxWeightLb: e.sets.length ? Math.max(...e.sets.map(s => s.weightLb)) : null, volumeLb: e.sets.reduce((v, s) => v + s.reps * s.weightLb, 0), sets: e.sets })));
  return structuredClone(result) as T;
}
