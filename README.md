# Workout

Workout is a mobile-first app for workout partners to build consistency together. Weekly streaks take center stage, with a quick check-in and a shared view of each person's progress.

## Weekly streaks

- See each person's current streak, longest streak, workout days this week, and weekly goal.
- Follow a Monday–Sunday activity calendar for each person.
- Start with a goal of 3 workout days per week, adjustable from 1–7.
- Count workout days rather than sessions: several workouts on one day count as one day.
- Grow a streak by meeting the weekly goal. An unfinished current week preserves the previous streak; a missed past week breaks it.
- Change a goal starting the following Monday while keeping historical goals intact.
- Use New York time for workout dates and week boundaries.

## Quick check-ins and detailed logs

Tap **Check in today** to log the workout you just completed. The form starts with today's date and completion selected. Add a workout type, duration, notes, strength sets, or cardio details before saving; **Just check in** records today immediately without requiring workout details. Canceling leaves your activity unchanged. After checking in, use **Log another workout** to record another session that day.

- Log strength exercises with sets, reps, and weight in pounds, including bodyweight sets.
- Log cardio with duration and optional distance in miles.
- Save drafts without counting them toward a streak.
- Backdate workouts, correct entries, or delete your own sessions. Streaks update to reflect the changes.
- Completed workouts cannot be dated in the future.

## Shared history

View your workout partners' histories and filter by person or date range. Open a session to see its exercises, notes, and completion status. Each person can edit only their own workouts.

## Reusable templates

Save personal strength or cardio routines as templates. Reuse a template to prefill a new workout, then adjust it for the day. Template workouts begin as drafts and count toward your goal only when marked completed.

## Exercise progress

Explore an exercise's history for any workout partner. Charts and session records show strength weights and training volume, or cardio distance and duration.

## Personal settings

Choose green, red, blue, beige, or purple, each with light and dark modes. Each person's appearance preferences are saved independently. Settings also includes weekly goals and goal history.

The interface is designed for phones, with bottom navigation and touch-sized controls.

## Sign-in

Use Google to sign in to your private workout space. Your Google profile picture appears in the header and beside your weekly streak; initials appear when a picture is unavailable. Access is limited to invited accounts. Members can view shared workout activity, while workouts, templates, goals, and appearance settings remain individually editable.

## Accountability buddies

Select **Invite a friend** on Overview, or open **Accountability buddies** in Settings. Enter your friend's Google account email and copy the invitation link to share. Links expire after seven days. You can cancel a pending invitation or replace its link from Settings; replacement invalidates the previous link.

Your friend signs in with the matching verified Google email and accepts before activity is shared. Each accepted connection is mutual and includes historical workouts, notes, drafts, streaks, and exercise progress. Your buddies cannot see each other unless they connect separately. Every member can invite any number of buddies.

Either person can remove a buddy in Settings. Removal ends sharing in both directions but keeps both accounts, personal activity, and other connections. Reconnecting requires a new invitation. Previously loaded activity on an offline device is cleared when it next refreshes its connections; personal offline workout changes are preserved.

Set `PublicOrigin` to the application's public HTTPS origin in `.env` for invitation links shared outside your local environment. The included database migration preserves the original buddy pair. Back up the database and deploy the migration, backend, and frontend together. Do not configure proxy/access logs to record invitation tokens in URLs.

## Explore the demo

Select **Explore the demo** at the bottom right of the login page to try the app without signing in.

The demo includes generic sample identities, weekly streaks, workout histories, strength and cardio logs, drafts, templates, progress charts, goal history, and all appearance options. Try check-ins and edits without affecting real workouts.

Demo workout changes reset on refresh. Demo appearance choices are remembered in your browser. Select **Exit demo** to return to the login page.

## Install on your phone and enable notifications

Workout can be installed as a Progressive Web App without an App Store subscription. Follow [Install on iPhone](docs/install-on-iphone.md) for Home Screen installation, notifications, and offline logging. Operators should follow [PWA setup](docs/pwa-setup.md) for deployment, VAPID keys, verification, and troubleshooting.
