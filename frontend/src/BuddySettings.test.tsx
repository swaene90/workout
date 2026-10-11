import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import BuddySettings from "./BuddySettings";
import { request } from "./api";
vi.mock("./api", () => ({ request: vi.fn() }));
const link = {
  id: "invite-1",
  email: "friend@example.com",
  expires: "2026-10-17T12:00:00Z",
  link: "https://workout.example/invite/secret",
};
beforeEach(() => {
  vi.resetAllMocks();
});
test("creates a pending invitation and provides a copyable link", async () => {
  const user = userEvent.setup();
  let created = false;
  vi.mocked(request).mockImplementation(async (_path, method) => {
    if (method === "POST") {
      created = true;
      return link as never;
    }
    return {
      buddies: [],
      invitations: created ? [{ ...link, status: "pending" }] : [],
    } as never;
  });
  const onChange = vi.fn().mockResolvedValue(undefined);
  render(<BuddySettings onChange={onChange} />);
  await user.type(
    screen.getByLabelText("Friend’s Google account email"),
    "friend@example.com",
  );
  await user.click(screen.getByRole("button", { name: "Invite a friend" }));
  expect(
    await screen.findByLabelText(
      "Share this invitation with friend@example.com",
    ),
  ).toHaveValue(link.link);
  expect(request).toHaveBeenCalledWith("/buddy-invitations", "POST", {
    email: "friend@example.com",
  });
  expect(
    await screen.findByText("friend@example.com · Pending"),
  ).toBeInTheDocument();
  await user.click(
    screen.getByRole("button", { name: "Copy invitation link" }),
  );
  expect(
    await screen.findByRole("button", { name: "Copied" }),
  ).toBeInTheDocument();
  expect(onChange).toHaveBeenCalled();
});
test("removing a buddy requires confirmation and refreshes sharing", async () => {
  let removed = false;
  vi.mocked(request).mockImplementation(async (_path, method) => {
    if (method === "DELETE") {
      removed = true;
      return undefined as never;
    }
    return {
      buddies: removed ? [] : [{ id: "friend", name: "Friend" }],
      invitations: [],
    } as never;
  });
  const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
  const onChange = vi.fn().mockResolvedValue(undefined);
  render(<BuddySettings onChange={onChange} />);
  await userEvent.click(
    await screen.findByRole("button", { name: "Remove buddy" }),
  );
  expect(removed).toBe(false);
  confirm.mockReturnValue(true);
  await userEvent.click(screen.getByRole("button", { name: "Remove buddy" }));
  await waitFor(() => expect(onChange).toHaveBeenCalled());
  expect(request).toHaveBeenCalledWith("/buddies/friend", "DELETE");
  expect(screen.queryByText("Friend")).not.toBeInTheDocument();
  confirm.mockRestore();
});
test("shows network errors without inventing an invitation", async () => {
  vi.mocked(request).mockImplementation(async (_path, method) => {
    if (method === "POST") throw new Error("The device is offline.");
    return { buddies: [], invitations: [] } as never;
  });
  render(<BuddySettings onChange={vi.fn()} />);
  await userEvent.type(
    screen.getByLabelText("Friend’s Google account email"),
    "friend@example.com",
  );
  await userEvent.click(
    screen.getByRole("button", { name: "Invite a friend" }),
  );
  expect(await screen.findByRole("alert")).toHaveTextContent("offline");
  expect(
    screen.queryByRole("button", { name: "Copy invitation link" }),
  ).not.toBeInTheDocument();
});
