import { createRouter, createWebHistory, type RouteRecordRaw } from "vue-router";
import { useAuthStore } from "@/stores/auth";

const routes: RouteRecordRaw[] = [
  {
    path: "/",
    name: "brand-picker",
    component: () => import("@/pages/BrandPicker.vue"),
    meta: { requiresAuth: true },
  },
  {
    path: "/brands/:brandId",
    component: () => import("@/pages/brands/BrandLayout.vue"),
    meta: { requiresAuth: true },
    children: [
      { path: "", redirect: (to) => `/brands/${to.params.brandId}/tree` },
      {
        path: "tree",
        name: "brand-tree",
        component: () => import("@/pages/brands/Tree.vue"),
      },
      {
        path: "invitations",
        name: "brand-invitations",
        component: () => import("@/pages/brands/Invitations.vue"),
      },
      {
        path: "grants",
        name: "brand-grants",
        component: () => import("@/pages/brands/Grants.vue"),
      },
      {
        path: "audit",
        name: "brand-audit",
        component: () => import("@/pages/brands/Audit.vue"),
      },
    ],
  },
  {
    path: "/platform",
    component: () => import("@/pages/platform/PlatformLayout.vue"),
    meta: { requiresAuth: true, requiresSuperAdmin: true },
    children: [
      { path: "", redirect: "/platform/subjects" },
      {
        path: "subjects",
        name: "platform-subjects",
        component: () => import("@/pages/platform/Subjects.vue"),
      },
      {
        path: "config",
        name: "platform-config",
        component: () => import("@/pages/platform/Config.vue"),
      },
      {
        path: "audit",
        name: "platform-audit",
        component: () => import("@/pages/platform/PlatformAudit.vue"),
      },
    ],
  },
  {
    path: "/accept-invite",
    name: "accept-invite",
    component: () => import("@/pages/AcceptInvite.vue"),
    meta: { requiresAuth: true },
  },
  {
    path: "/auth/callback",
    name: "auth-callback",
    component: () => import("@/pages/AuthCallback.vue"),
  },
  {
    path: "/auth/silent",
    name: "auth-silent",
    component: () => import("@/pages/AuthSilent.vue"),
  },
  {
    path: "/:pathMatch(.*)*",
    component: () => import("@/pages/NotFound.vue"),
  },
];

export const router = createRouter({
  history: createWebHistory(),
  routes,
});

router.beforeEach(async (to) => {
  if (!to.meta.requiresAuth) return true;
  const auth = useAuthStore();
  if (!auth.isAuthenticated) {
    await auth.login(to.fullPath);
    return false;
  }
  if (to.meta.requiresSuperAdmin && !auth.isSuperAdmin) {
    return "/";
  }
  return true;
});
