import { useState } from "react";
import { Check, Moon, Sun } from "lucide-react";
import { api, demoMode } from "./api";
import { applyAppearance, themes } from "./appearance";
import type { Appearance } from "./appearance";

export default function AppearanceSettings({
  value,
  onChange,
}: {
  value: Appearance;
  onChange: (value: Appearance) => void;
}) {
  const [saving, setSaving] = useState(false);
  const [status, setStatus] = useState("");
  const [error, setError] = useState("");
  const update = async (next: Appearance) => {
    if (saving || (next.theme === value.theme && next.mode === value.mode))
      return;
    const previous = value;
    setSaving(true);
    setError("");
    setStatus("");
    onChange(next);
    applyAppearance(next);
    try {
      await api<Appearance>("/preferences", "PUT", next);
      setStatus("Appearance saved.");
    } catch (e) {
      onChange(previous);
      applyAppearance(previous);
      setError((e as Error).message);
    } finally {
      setSaving(false);
    }
  };
  return (
    <section
      className="panel settings-panel appearance-panel"
      aria-labelledby="appearance-heading"
    >
      <div className="section-heading">
        <h2 id="appearance-heading">Your colors, your vibe</h2>
        <span className="tag">Appearance</span>
      </div>
      <p className="muted">
        A softer shade of showing up. Choose your color and make it light or
        dark.
      </p>
      <fieldset disabled={saving} className="theme-fieldset">
        <legend>Color theme</legend>
        <div className="theme-options">
          {themes.map((theme) => (
            <label
              className={`theme-option ${value.theme === theme ? "selected" : ""}`}
              key={theme}
            >
              <input
                type="radio"
                name="color-theme"
                value={theme}
                checked={value.theme === theme}
                onChange={() => void update({ ...value, theme })}
              />
              <span
                className={`theme-swatch swatch-${theme}`}
                aria-hidden="true"
              >
                {value.theme === theme && <Check size={20} />}
              </span>
              <span>{theme[0].toUpperCase() + theme.slice(1)}</span>
            </label>
          ))}
        </div>
      </fieldset>
      <fieldset disabled={saving} className="theme-fieldset">
        <legend>Display mode</legend>
        <div className="mode-options">
          {(["light", "dark"] as const).map((mode) => (
            <label
              className={`mode-option ${value.mode === mode ? "selected" : ""}`}
              key={mode}
            >
              <input
                type="radio"
                name="display-mode"
                value={mode}
                checked={value.mode === mode}
                onChange={() => void update({ ...value, mode })}
              />
              {mode === "light" ? <Sun size={18} /> : <Moon size={18} />}
              <span>{mode === "light" ? "Light" : "Dark"}</span>
              {value.mode === mode && (
                <Check size={15} className="mode-check" />
              )}
            </label>
          ))}
        </div>
      </fieldset>
      <p className="appearance-status" role="status">
        {saving
          ? "Saving appearance…"
          : status ||
            (demoMode
              ? "Remembered in this browser for the preview."
              : "Saved to your account. Each of you can choose your own.")}
      </p>
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
