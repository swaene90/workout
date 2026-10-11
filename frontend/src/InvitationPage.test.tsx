import { render, screen } from "@testing-library/react";
import { vi } from "vitest";
import InvitationPage from "./InvitationPage";
import { request, setCsrf } from "./api";
vi.mock("./api", () => ({ request: vi.fn(), setCsrf: vi.fn() }));
beforeEach(() => vi.resetAllMocks());
test("explains sharing before Google sign-in", async () => {
  vi.mocked(request).mockResolvedValue({
    inviterName: "Friend",
    status: "pending",
    authenticated: false,
    matchesEmail: false,
    csrfToken: "csrf",
  });
  render(<InvitationPage token="abc" />);
  expect(
    await screen.findByRole("link", { name: "Continue with Google" }),
  ).toHaveAttribute("href", "/auth/login?invite=abc");
  expect(screen.getByText(/past and future workouts/)).toBeInTheDocument();
  expect(setCsrf).toHaveBeenCalledWith("csrf");
});
test("wrong-account sessions cannot accept", async () => {
  vi.mocked(request).mockResolvedValue({
    inviterName: "Friend",
    status: "pending",
    authenticated: true,
    matchesEmail: false,
    csrfToken: "csrf",
  });
  render(<InvitationPage token="abc" />);
  expect(
    await screen.findByText(/Sign in with the Google email/),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Accept invitation" }),
  ).not.toBeInTheDocument();
});
test("expiration prevents acceptance", async () => {
  vi.mocked(request).mockResolvedValue({
    inviterName: "Friend",
    status: "expired",
    authenticated: true,
    matchesEmail: true,
    csrfToken: "csrf",
  });
  render(<InvitationPage token="abc" />);
  expect(
    await screen.findByText(/This invitation is expired/),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Accept invitation" }),
  ).not.toBeInTheDocument();
});
