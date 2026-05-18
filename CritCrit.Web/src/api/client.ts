import createClient, { type Middleware } from "openapi-fetch";
import { userManager } from "@/auth/oidc";
import type { paths } from "./generated";

const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const u = await userManager.getUser();
    if (u && !u.expired) {
      request.headers.set("Authorization", `Bearer ${u.access_token}`);
    }
    return request;
  },
  async onResponse({ response }) {
    if (response.status === 401) {
      await userManager.signinRedirect({ state: { returnTo: window.location.pathname } });
    }
    return response;
  },
};

export const api = createClient<paths>({ baseUrl: import.meta.env.VITE_API_URL });
api.use(authMiddleware);

export async function authenticatedFetch(input: string, init: RequestInit = {}) {
  const u = await userManager.getUser();
  const headers = new Headers(init.headers);
  if (u && !u.expired) headers.set("Authorization", `Bearer ${u.access_token}`);
  return fetch(`${import.meta.env.VITE_API_URL}${input}`, { ...init, headers });
}
