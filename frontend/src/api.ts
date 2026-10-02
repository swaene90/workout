export const demoMode = /^\/demo\/?$/.test(window.location.pathname);
let csrfToken = "";
let accountIdentity = "";
export function setAccountIdentity(id: string) {
  accountIdentity = id;
}
export function setCsrf(token: string) {
  csrfToken = token;
}
export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public problem?: Record<string, unknown>,
  ) {
    super(message);
  }
}
export async function request<T>(
  path: string,
  method = "GET",
  body?: unknown,
): Promise<T> {
  if (demoMode) {
    const { demoRequest } = await import("./demo");
    return demoRequest<T>(path, method, body);
  }
  if (!navigator.onLine) throw new TypeError("The device is offline.");
  const response = await fetch(`/api${path}`, {
    method,
    credentials: "same-origin",
    signal: AbortSignal.timeout(15000),
    headers: {
      ...(accountIdentity && path !== "/me"
        ? { "X-Workout-Account": accountIdentity }
        : {}),
      ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
      ...(method !== "GET" ? { "X-CSRF-TOKEN": csrfToken } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const details = problem.errors
      ? Object.values(problem.errors).flat().join(" ")
      : problem.detail || problem.title;
    throw new ApiError(
      response.status,
      details || "Something went wrong. Please try again.",
      problem,
    );
  }
  return response.status === 204 ? (undefined as T) : response.json();
}

export async function api<T>(
  path: string,
  method = "GET",
  body?: unknown,
): Promise<T> {
  if (demoMode) return request<T>(path, method, body);
  const { offlineRequest } = await import("./offline");
  return offlineRequest<T>(path, method, body);
}
