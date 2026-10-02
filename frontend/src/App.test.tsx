import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import App from "./App";
import { api, ApiError } from "./api";
vi.mock("./api", () => ({
  api: vi.fn(),
  setCsrf: vi.fn(),
  demoMode: false,
  ApiError: class extends Error {
    constructor(
      public status: number,
      message: string,
    ) {
      super(message);
    }
  },
}));
const member = { id: "you", name: "You", email: "swaene1@gmail.com" };
const summary = {
  currentStreak: 4,
  longestStreak: 5,
  workoutDaysThisWeek: 0,
  weeklyGoal: 3,
  nextWeeklyGoal: null,
  weekStart: "2026-09-28",
  calendar: Array.from({ length: 7 }, (_, i) => ({
    date: `2026-${i < 3 ? "09" : "10"}-${i < 3 ? 28 + i : `0${i - 2}`}`,
    workedOut: false,
  })),
  weeks: [],
};
beforeEach(() => {
  localStorage.clear();
  vi.resetAllMocks();
  vi.mocked(api).mockImplementation(async (path) => {
    if (path === "/me")
      return {
        user: member,
        csrfToken: "token",
        today: "2026-10-01",
        timeZone: "America/New_York",
      } as never;
    if (path === "/dashboard")
      return [
        { user: member, summary },
        { user: { ...member, id: "britt", name: "Britt" }, summary },
      ] as never;
    if (path.startsWith("/workouts?"))
      return { items: [], total: 0, page: 1 } as never;
    return [] as never;
  });
});
test("quick check-in records today without requiring detailed exercises", async () => {
  render(<App />);
  await userEvent.click(
    await screen.findByRole("button", { name: "Check in today" }),
  );
  await waitFor(() =>
    expect(api).toHaveBeenCalledWith(
      "/workouts",
      "POST",
      expect.objectContaining({
        date: "2026-10-01",
        completed: true,
        exercises: [],
      }),
    ),
  );
  expect(await screen.findByRole("status")).toHaveTextContent(
    "Today is checked in!",
  );
});
test("unauthenticated visitors see Google login rather than private data", async () => {
  vi.mocked(api).mockRejectedValue(new ApiError(401, "Unauthorized"));
  render(<App />);
  expect(
    await screen.findByRole("link", { name: /Continue with Google/ }),
  ).toHaveAttribute("href", "/auth/login");
  expect(screen.queryByText("Your streaks")).not.toBeInTheDocument();
  expect(
    screen.getByRole("link", { name: /Explore the demo/ }),
  ).toHaveAttribute("href", "/demo");
});
test("sign-out clears the private view without reloading protected data", async () => {
  render(<App />);
  await userEvent.click(
    await screen.findByRole("button", { name: "Settings" }),
  );
  await userEvent.click(screen.getByRole("button", { name: "Sign out" }));
  expect(
    await screen.findByRole("link", { name: /Continue with Google/ }),
  ).toBeInTheDocument();
  expect(api).toHaveBeenCalledWith("/logout", "POST");
});
