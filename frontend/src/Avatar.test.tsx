import { fireEvent, render, screen } from "@testing-library/react";
import Avatar from "./Avatar";
const user = { id: "sample", name: "Sample", email: "sample@demo.example" };
test("missing or failed photos show initials and a new photo can load", () => {
  const { container, rerender } = render(<Avatar user={user} />);
  expect(screen.getByRole("img")).toHaveTextContent("S");
  rerender(
    <Avatar
      user={{
        ...user,
        profilePictureUrl: "https://lh3.googleusercontent.com/photo",
      }}
    />,
  );
  const photo = container.querySelector("img")!;
  expect(photo).toHaveAttribute("referrerpolicy", "no-referrer");
  fireEvent.error(photo);
  expect(container.querySelector("img")).toBeNull();
  expect(screen.getByRole("img")).toHaveTextContent("S");
  rerender(
    <Avatar
      user={{
        ...user,
        profilePictureUrl: "https://lh3.googleusercontent.com/new",
      }}
    />,
  );
  expect(container.querySelector("img")).toHaveAttribute(
    "src",
    "https://lh3.googleusercontent.com/new",
  );
});
