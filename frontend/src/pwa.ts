/// <reference types="vite-plugin-pwa/client" />
import { registerSW } from "virtual:pwa-register";
export const updateApp = registerSW({
  onNeedRefresh() {
    window.dispatchEvent(new Event("workout-update"));
  },
});
