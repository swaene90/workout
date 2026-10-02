import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { vi } from "vitest";
import AppearanceSettings from "./AppearanceSettings";
import { api } from "./api";
import {
  applyAppearance,
  defaultAppearance,
  readAppearance,
  themes,
} from "./appearance";
import type { Appearance } from "./appearance";
vi.mock("./api", () => ({ api: vi.fn(), demoMode: false }));
function Settings() {
  const [value, setValue] = useState<Appearance>(defaultAppearance);
  return <AppearanceSettings value={value} onChange={setValue} />;
}
beforeEach(() => {
  vi.resetAllMocks();
  localStorage.clear();
  applyAppearance(defaultAppearance);
});
test.each(themes)(
  "%s supports light and dark modes and persists the selection",
  async (theme) => {
    const user = userEvent.setup();
    render(<Settings />);
    await user.click(
      screen.getByRole("radio", {
        name: theme[0].toUpperCase() + theme.slice(1),
      }),
    );
    await user.click(screen.getByRole("radio", { name: "Dark" }));
    expect(document.documentElement).toHaveAttribute("data-theme", theme);
    expect(document.documentElement).toHaveAttribute("data-mode", "dark");
    expect(readAppearance()).toEqual({ theme, mode: "dark" });
    expect(api).toHaveBeenLastCalledWith("/preferences", "PUT", {
      theme,
      mode: "dark",
    });
    await user.click(screen.getByRole("radio", { name: "Light" }));
    expect(readAppearance()).toEqual({ theme, mode: "light" });
  },
);
test("failed saves restore the previous appearance", async () => {
  vi.mocked(api).mockRejectedValue(new Error("Could not save. Try again."));
  render(<Settings />);
  await userEvent.click(screen.getByRole("radio", { name: "Purple" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("Could not save.");
  expect(readAppearance()).toEqual(defaultAppearance);
  expect(screen.getByRole("radio", { name: "Green" })).toBeChecked();
});
test("invalid cached appearance safely falls back to green light", () => {
  localStorage.setItem(
    "workout.appearance",
    '{"theme":"invalid","mode":"dark"}',
  );
  expect(readAppearance()).toEqual(defaultAppearance);
  localStorage.setItem("workout.appearance", "invalid json");
  expect(readAppearance()).toEqual(defaultAppearance);
});
