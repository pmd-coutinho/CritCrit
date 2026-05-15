import { UserManager, WebStorageStateStore, Log } from "oidc-client-ts";

if (import.meta.env.DEV) {
  Log.setLogger(console);
  Log.setLevel(Log.WARN);
}

const origin = window.location.origin;

export const userManager = new UserManager({
  authority: `${import.meta.env.VITE_KEYCLOAK_URL}/realms/${import.meta.env.VITE_KEYCLOAK_REALM}`,
  client_id: import.meta.env.VITE_KEYCLOAK_CLIENT_ID,
  redirect_uri: `${origin}/auth/callback`,
  silent_redirect_uri: `${origin}/auth/silent`,
  post_logout_redirect_uri: `${origin}/`,
  response_type: "code",
  scope: "openid profile email",
  automaticSilentRenew: true,
  loadUserInfo: true,
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
});

export type SessionUser = {
  subjectId: string;
  username: string;
  email?: string;
  name?: string;
  roles: string[];
  accessToken: string;
  expiresAt: number;
};

export function mapUser(u: import("oidc-client-ts").User): SessionUser {
  const profile = u.profile as Record<string, unknown>;
  const realm = (profile["realm_access"] as { roles?: string[] } | undefined)?.roles ?? [];
  return {
    subjectId: u.profile.sub ?? "",
    username: (profile["preferred_username"] as string) ?? u.profile.sub ?? "",
    email: u.profile.email,
    name: u.profile.name,
    roles: realm,
    accessToken: u.access_token,
    expiresAt: (u.expires_at ?? 0) * 1000,
  };
}
