export type Member = {
  id: string;
  name: string;
  email: string;
  theme?: string;
  mode?: string;
};
export type StrengthSet = { reps: number; weightLb: number };
export type Exercise = {
  name: string;
  kind: "Strength" | "Cardio";
  sets: StrengthSet[];
  durationMinutes: number | null;
  distanceMiles: number | null;
};
export type Workout = {
  id: string;
  userId: string;
  date: string;
  title: string;
  type: string;
  completed: boolean;
  durationMinutes: number | null;
  notes: string;
  exercises: Exercise[];
};
export type WorkoutInput = Omit<Workout, "id" | "userId">;
export type Template = {
  id: string;
  userId: string;
  name: string;
  type: string;
  notes: string;
  exercises: Exercise[];
};
export type Goal = { userId: string; effectiveWeek: string; days: number };
export type Dashboard = {
  user: Member;
  summary: {
    currentStreak: number;
    longestStreak: number;
    workoutDaysThisWeek: number;
    weeklyGoal: number;
    nextWeeklyGoal: number | null;
    weekStart: string;
    calendar: { date: string; workedOut: boolean }[];
    weeks: { start: string; days: number; goal: number; met: boolean }[];
  };
};
export type Me = {
  user: Member;
  csrfToken: string;
  today: string;
  timeZone: string;
};
export type History = { items: Workout[]; total: number; page: number };
export type ProgressPoint = {
  id: string;
  date: string;
  kind: string;
  durationMinutes: number | null;
  distanceMiles: number | null;
  maxWeightLb: number | null;
  volumeLb: number;
  sets: StrengthSet[];
};
