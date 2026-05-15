<script setup lang="ts">
import { computed } from "vue";
import { RouterLink, RouterView, useRoute } from "vue-router";
import { useAuthStore } from "@/stores/auth";
import { useThemeStore } from "@/stores/theme";

const route = useRoute();
const auth = useAuthStore();
const theme = useThemeStore();

const brandId = computed(() => route.params.brandId as string);

const tabs = computed(() => [
  { to: `/brands/${brandId.value}/tree`, label: "Tree" },
  { to: `/brands/${brandId.value}/invitations`, label: "Invitations" },
  { to: `/brands/${brandId.value}/grants`, label: "Grants" },
  { to: `/brands/${brandId.value}/audit`, label: "Audit" },
]);
</script>

<template>
  <div class="min-h-screen">
    <header class="flex h-12 items-center justify-between border-b border-border px-6">
      <div class="flex items-center gap-6">
        <RouterLink to="/" class="flex items-center gap-2 font-mono text-xs uppercase tracking-[0.18em] text-fg-muted hover:text-fg">
          <span class="inline-block h-1.5 w-1.5 rounded-full bg-accent" />
          critcrit
        </RouterLink>
        <span class="font-mono text-xs text-fg-subtle">/</span>
        <span class="font-mono text-xs text-fg">{{ brandId }}</span>
      </div>
      <div class="flex items-center gap-3 text-xs text-fg-muted">
        <span>{{ auth.user?.name ?? auth.user?.username }}</span>
        <button class="hover:text-fg" @click="theme.toggle()">{{ theme.mode === "dark" ? "☾" : "☀" }}</button>
        <button class="hover:text-fg" @click="auth.logout()">sign out</button>
      </div>
    </header>

    <nav class="flex h-10 items-center gap-1 border-b border-border px-4">
      <RouterLink
        v-for="tab in tabs"
        :key="tab.to"
        :to="tab.to"
        class="relative inline-flex h-10 items-center px-3 text-sm text-fg-muted transition-colors hover:text-fg"
        active-class="text-fg [&>span]:scale-x-100"
      >
        {{ tab.label }}
        <span class="absolute inset-x-3 bottom-0 h-px origin-center scale-x-0 bg-accent transition-transform" />
      </RouterLink>
    </nav>

    <main class="px-6 py-8">
      <RouterView />
    </main>
  </div>
</template>
