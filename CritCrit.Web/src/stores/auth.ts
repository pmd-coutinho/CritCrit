import { defineStore } from "pinia";
import { computed, ref } from "vue";
import { mapUser, userManager, type SessionUser } from "@/auth/oidc";

export const useAuthStore = defineStore("auth", () => {
  const user = ref<SessionUser | null>(null);
  const ready = ref(false);

  const isAuthenticated = computed(() => !!user.value);
  const isSuperAdmin = computed(() => user.value?.roles.includes("critcrit.superadmin") ?? false);

  async function initialize() {
    if (ready.value) return;
    try {
      const existing = await userManager.getUser();
      if (existing && !existing.expired) {
        user.value = mapUser(existing);
      }
    } catch {
      user.value = null;
    }
    userManager.events.addUserLoaded((u) => {
      user.value = mapUser(u);
    });
    userManager.events.addUserUnloaded(() => {
      user.value = null;
    });
    userManager.events.addAccessTokenExpired(() => {
      userManager.signinSilent().catch(() => userManager.signinRedirect());
    });
    ready.value = true;
  }

  async function login(returnTo?: string) {
    await userManager.signinRedirect({ state: { returnTo: returnTo ?? window.location.pathname } });
  }

  async function logout() {
    await userManager.signoutRedirect();
  }

  async function completeRedirect(): Promise<string> {
    const u = await userManager.signinRedirectCallback();
    user.value = mapUser(u);
    const state = (u.state as { returnTo?: string } | null) ?? null;
    return state?.returnTo ?? "/";
  }

  async function completeSilent() {
    await userManager.signinSilentCallback();
  }

  return {
    user,
    ready,
    isAuthenticated,
    isSuperAdmin,
    initialize,
    login,
    logout,
    completeRedirect,
    completeSilent,
  };
});
