import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import InvitationPage from "./InvitationPage";
import "./styles.css";
import "./themes.css";
import { applyAppearance, readAppearance } from "./appearance";
applyAppearance(readAppearance());
window.addEventListener("storage", (event) => {
  if (event.key === "workout-account-change") location.reload();
});
ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    {location.pathname.startsWith("/invite/") ? (
      <InvitationPage token={location.pathname.slice(8)} />
    ) : (
      <App />
    )}
  </React.StrictMode>,
);

import "./pwa";
