/// <reference lib="webworker" />
import {
  precacheAndRoute,
  cleanupOutdatedCaches,
  createHandlerBoundToURL,
} from "workbox-precaching";
import { registerRoute, NavigationRoute } from "workbox-routing";
declare const self: ServiceWorkerGlobalScope & {
  __WB_MANIFEST: Array<{ url: string; revision: string | null }>;
};
precacheAndRoute(self.__WB_MANIFEST);
cleanupOutdatedCaches();
registerRoute(
  new NavigationRoute(createHandlerBoundToURL("/index.html"), {
    denylist: [
      /^\/api(?:\/|$)/,
      /^\/auth(?:\/|$)/,
      /^\/signin-google/,
      /^\/health(?:\/|$)/,
      /^\/docs\//,
    ],
  }),
);
self.addEventListener("message", (event) => {
  if (event.data?.type === "SKIP_WAITING") void self.skipWaiting();
});
self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
});
self.addEventListener("push", (event) => {
  let data: Record<string, string> = {};
  try {
    data = event.data?.json() ?? {};
  } catch {
    /* Display a safe fallback. */
  }
  event.waitUntil(
    self.registration.showNotification(data.title || "Workout", {
      body: data.body || "Open Workout to see your progress.",
      icon: "/icons/icon-192.png",
      badge: "/icons/icon-192.png",
      tag: data.tag || "workout",
      data: { url: data.url || "/" },
    }),
  );
});
self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  event.waitUntil(
    (async () => {
      const candidate = new URL(
        event.notification.data?.url || "/",
        self.location.origin,
      );
      const url =
        candidate.origin === self.location.origin && candidate.pathname === "/"
          ? candidate.href
          : self.location.origin + "/";
      const windows = await self.clients.matchAll({
        type: "window",
        includeUncontrolled: true,
      });
      const client = windows.find(
        (c) => new URL(c.url).origin === self.location.origin,
      ) as WindowClient | undefined;
      if (client) {
        await client.navigate(url);
        await client.focus();
      } else await self.clients.openWindow(url);
    })(),
  );
});
