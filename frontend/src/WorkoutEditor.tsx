import { useState } from "react";
import { ArrowLeft, Plus, Trash2, Dumbbell, Footprints } from "lucide-react";
import type { Exercise, Template, Workout, WorkoutInput } from "./types";
import { api } from "./api";

export type EditorState = {
  checkIn?: boolean;
  workout?: Workout;
  template?: Template;
  templateMode?: boolean;
  fromTemplate?: Template;
};
export default function WorkoutEditor({
  editor,
  today,
  onSaved,
  onCancel,
}: {
  editor: EditorState;
  today: string;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const source = editor.workout || editor.template || editor.fromTemplate;
  const templateMode = !!editor.templateMode;
  const [form, setForm] = useState<WorkoutInput>(() => ({
    date: editor.workout?.date ?? today,
    title:
      editor.workout?.title ??
      editor.template?.name ??
      editor.fromTemplate?.name ??
      (editor.checkIn ? "Daily check-in" : ""),
    type: source?.type ?? (editor.checkIn ? "Other" : "Mixed"),
    completed: editor.workout?.completed ?? !editor.fromTemplate,
    durationMinutes: editor.workout?.durationMinutes ?? null,
    notes: source?.notes ?? "",
    exercises: structuredClone(source?.exercises ?? []),
  }));
  const [saving, setSaving] = useState(false),
    [error, setError] = useState("");
  const patchExercise = (i: number, patch: Partial<Exercise>) =>
    setForm((f) => ({
      ...f,
      exercises: f.exercises.map((e, n) => (n === i ? { ...e, ...patch } : e)),
    }));
  const number = (value: string) => (value === "" ? null : Number(value));
  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setSaving(true);
    setError("");
    try {
      if (templateMode)
        await api(
          `/templates${editor.template ? `/${editor.template.id}` : ""}`,
          editor.template ? "PUT" : "POST",
          {
            name: form.title,
            type: form.type,
            notes: form.notes,
            exercises: form.exercises,
          },
        );
      else
        await api(
          `/workouts${editor.workout ? `/${editor.workout.id}` : ""}`,
          editor.workout ? "PUT" : "POST",
          form,
        );
      onSaved();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  };
  return (
    <section className="editor">
      <button className="text-button back" onClick={onCancel}>
        <ArrowLeft size={16} /> Back
      </button>
      <div className="page-title">
        <div>
          <p className="eyebrow">
            {templateMode ? "MAKE IT REPEATABLE" : "EVERY SESSION COUNTS"}
          </p>
          <h1>
            {templateMode
              ? editor.template
                ? "Edit template"
                : "New template"
              : editor.workout
                ? "Edit workout"
                : "Log a workout"}
          </h1>
        </div>
      </div>
      <form onSubmit={submit} className="panel form-panel">
        <div className="form-grid">
          <label>
            {templateMode ? "Template name" : "Workout name"}
            <input
              required
              maxLength={120}
              placeholder="e.g. Upper body day"
              value={form.title}
              onChange={(e) => setForm({ ...form, title: e.target.value })}
            />
          </label>
          <label>
            Workout type
            <select
              value={form.type}
              onChange={(e) => setForm({ ...form, type: e.target.value })}
            >
              {["Strength", "Cardio", "Mixed", "Other"].map((t) => (
                <option key={t}>{t}</option>
              ))}
            </select>
          </label>
          {!templateMode && (
            <>
              <label>
                Date
                <input
                  type="date"
                  required
                  min="1970-01-05"
                  max={form.completed ? today : undefined}
                  value={form.date}
                  onChange={(e) => setForm({ ...form, date: e.target.value })}
                />
              </label>
              <label>
                Duration (minutes)
                <input
                  type="number"
                  min="0.01"
                  max="1440"
                  step="0.01"
                  value={form.durationMinutes ?? ""}
                  onChange={(e) =>
                    setForm({
                      ...form,
                      durationMinutes: number(e.target.value),
                    })
                  }
                />
              </label>
            </>
          )}
        </div>
        {!templateMode && (
          <label className="checkbox">
            <input
              type="checkbox"
              checked={form.completed}
              onChange={(e) =>
                setForm({ ...form, completed: e.target.checked })
              }
            />{" "}
            Completed · counts toward my weekly goal
          </label>
        )}
        <div className="section-heading">
          <h2>Exercises</h2>
          <span className="muted">Optional for a check-in</span>
        </div>
        {form.exercises.map((exercise, i) => (
          <div className="exercise-editor" key={i}>
            <div className="exercise-heading">
              <span className="exercise-number">{i + 1}</span>
              <label className="grow">
                Exercise name
                <input
                  required
                  maxLength={100}
                  placeholder="e.g. Bench press"
                  value={exercise.name}
                  onChange={(e) => patchExercise(i, { name: e.target.value })}
                />
              </label>
              <button
                type="button"
                className="icon-button danger"
                aria-label={`Remove exercise ${i + 1}`}
                onClick={() =>
                  setForm({
                    ...form,
                    exercises: form.exercises.filter((_, n) => n !== i),
                  })
                }
              >
                <Trash2 size={17} />
              </button>
            </div>
            {exercise.kind === "Strength" ? (
              <>
                <div className="set-labels">
                  <span>SET</span>
                  <span>REPS</span>
                  <span>WEIGHT (LB)</span>
                  <span />
                </div>
                {exercise.sets.map((set, j) => (
                  <div className="set-row" key={j}>
                    <span>{j + 1}</span>
                    <input
                      aria-label={`Exercise ${i + 1} set ${j + 1} reps`}
                      type="number"
                      required
                      min="1"
                      max="1000"
                      value={set.reps}
                      onChange={(e) =>
                        patchExercise(i, {
                          sets: exercise.sets.map((s, n) =>
                            n === j
                              ? { ...s, reps: Number(e.target.value) }
                              : s,
                          ),
                        })
                      }
                    />
                    <input
                      aria-label={`Exercise ${i + 1} set ${j + 1} weight`}
                      type="number"
                      required
                      min="0"
                      max="10000"
                      step="0.01"
                      value={set.weightLb}
                      onChange={(e) =>
                        patchExercise(i, {
                          sets: exercise.sets.map((s, n) =>
                            n === j
                              ? { ...s, weightLb: Number(e.target.value) }
                              : s,
                          ),
                        })
                      }
                    />
                    <button
                      type="button"
                      className="icon-button"
                      aria-label={`Remove set ${j + 1} from exercise ${i + 1}`}
                      onClick={() =>
                        patchExercise(i, {
                          sets: exercise.sets.filter((_, n) => n !== j),
                        })
                      }
                    >
                      <Trash2 size={15} />
                    </button>
                  </div>
                ))}
                <button
                  type="button"
                  className="text-button"
                  onClick={() =>
                    patchExercise(i, {
                      sets: [...exercise.sets, { reps: 8, weightLb: 0 }],
                    })
                  }
                >
                  <Plus size={14} /> Add set
                </button>
              </>
            ) : (
              <div className="form-grid">
                <label>
                  Duration (minutes)
                  <input
                    type="number"
                    required
                    min="0.01"
                    max="1440"
                    step="0.01"
                    value={exercise.durationMinutes ?? ""}
                    onChange={(e) =>
                      patchExercise(i, {
                        durationMinutes: number(e.target.value),
                      })
                    }
                  />
                </label>
                <label>
                  Distance (miles)
                  <input
                    type="number"
                    min="0"
                    max="1000"
                    step="0.001"
                    value={exercise.distanceMiles ?? ""}
                    onChange={(e) =>
                      patchExercise(i, {
                        distanceMiles: number(e.target.value),
                      })
                    }
                  />
                </label>
              </div>
            )}
          </div>
        ))}
        <div className="button-row">
          <button
            type="button"
            className="button secondary"
            onClick={() =>
              setForm({
                ...form,
                exercises: [
                  ...form.exercises,
                  {
                    name: "",
                    kind: "Strength",
                    sets: [{ reps: 8, weightLb: 0 }],
                    durationMinutes: null,
                    distanceMiles: null,
                  },
                ],
              })
            }
          >
            <Dumbbell size={16} /> Add strength
          </button>
          <button
            type="button"
            className="button secondary"
            onClick={() =>
              setForm({
                ...form,
                exercises: [
                  ...form.exercises,
                  {
                    name: "",
                    kind: "Cardio",
                    sets: [],
                    durationMinutes: 30,
                    distanceMiles: null,
                  },
                ],
              })
            }
          >
            <Footprints size={16} /> Add cardio
          </button>
        </div>
        <label className="notes">
          Notes
          <textarea
            maxLength={4000}
            rows={3}
            placeholder="How did it feel?"
            value={form.notes}
            onChange={(e) => setForm({ ...form, notes: e.target.value })}
          />
        </label>
        {error && (
          <p className="error" role="alert">
            {error}
          </p>
        )}
        <div className="form-footer">
          <button type="button" className="button secondary" onClick={onCancel}>
            Cancel
          </button>
          <button className="button primary" disabled={saving}>
            {saving
              ? "Saving…"
              : templateMode
                ? "Save template"
                : "Save workout"}
          </button>
        </div>
      </form>
    </section>
  );
}
