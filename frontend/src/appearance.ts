export const themes = ["green", "red", "blue", "beige", "purple"] as const;
export type Theme = (typeof themes)[number];
export type Mode = "light" | "dark";
export type Appearance = { theme: Theme; mode: Mode };
export const defaultAppearance: Appearance = { theme: "green", mode: "light" };
export const appearanceKey = /^\/demo\/?$/.test(window.location.pathname)
  ? "workout.demo.appearance"
  : "workout.appearance";

export function isAppearance(value: unknown): value is Appearance {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<Appearance>;
  return (
    themes.includes(candidate.theme as Theme) &&
    (candidate.mode === "light" || candidate.mode === "dark")
  );
}

export function readAppearance(): Appearance {
  try {
    const value: unknown = JSON.parse(
      localStorage.getItem(appearanceKey) ?? "null",
    );
    return isAppearance(value) ? value : { ...defaultAppearance };
  } catch {
    return { ...defaultAppearance };
  }
}

export function applyAppearance(value: Appearance) {
  document.documentElement.dataset.theme = value.theme;
  document.documentElement.dataset.mode = value.mode;
  const browserColor = document.querySelector<HTMLMetaElement>(
    'meta[name="theme-color"]',
  );
  if (browserColor) {
    browserColor.content = getComputedStyle(
      document.documentElement,
    ).backgroundColor;
  }
  try {
    localStorage.setItem(appearanceKey, JSON.stringify(value));
  } catch {
    /* Account persistence still works when storage is unavailable. */
  }
}
