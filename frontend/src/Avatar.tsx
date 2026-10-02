import { useState } from "react";
import type { Member } from "./types";

export default function Avatar({
  user,
  className = "",
}: {
  user: Member;
  className?: string;
}) {
  const [failedUrl, setFailedUrl] = useState<string | null>(null);
  const picture = user.profilePictureUrl;
  return (
    <span
      className={`avatar ${className}`}
      role="img"
      aria-label={`${user.name}'s profile picture`}
    >
      {picture && picture !== failedUrl ? (
        <img
          src={picture}
          alt=""
          referrerPolicy="no-referrer"
          onError={() => setFailedUrl(picture)}
        />
      ) : (
        user.name[0]
      )}
    </span>
  );
}
