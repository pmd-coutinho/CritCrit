<script setup lang="ts">
import { computed } from "vue";
import { useRoute, RouterLink } from "vue-router";
import { useAcceptInvite } from "@/api/queries";
import { errorMessage } from "@/api/errors";
import Badge from "@/components/ui/Badge.vue";

const route = useRoute();
const token = computed(() => (route.query.token as string) ?? "");
const { data, isLoading, error } = useAcceptInvite(token);

</script>

<template>
  <div class="flex min-h-screen items-center justify-center px-6">
    <div class="w-full max-w-md rounded-lg border border-border bg-surface p-6">
      <p class="font-mono text-xs uppercase tracking-widest text-fg-subtle">Invitation</p>

      <template v-if="!token">
        <h1 class="mt-2 text-xl font-semibold text-danger">Missing token</h1>
        <p class="mt-2 text-sm text-fg-muted">This link is incomplete.</p>
      </template>

      <template v-else-if="isLoading">
        <h1 class="mt-2 text-xl font-semibold text-fg">Accepting…</h1>
        <p class="mt-2 text-sm text-fg-muted">Granting access and applying any pending invitations.</p>
        <div class="mt-4 skeleton h-9" />
      </template>

      <template v-else-if="error">
        <h1 class="mt-2 text-xl font-semibold text-danger">Could not accept</h1>
        <p class="mt-2 text-sm text-fg-muted">{{ errorMessage(error) }}</p>
        <RouterLink to="/" class="mt-4 inline-block text-sm text-accent hover:underline">← back</RouterLink>
      </template>

      <template v-else-if="data">
        <h1 class="mt-2 text-xl font-semibold text-fg">You’re in.</h1>
        <ul class="mt-4 space-y-2 text-sm">
          <li class="flex items-center justify-between gap-2">
            <span class="text-fg-muted">Status</span>
            <Badge tone="success">{{ data.status }}</Badge>
          </li>
          <li class="flex items-center justify-between gap-2">
            <span class="text-fg-muted">Grant created</span>
            <span class="font-mono text-xs text-fg">{{ data.grantCreated ? "yes" : "no" }}</span>
          </li>
          <li class="flex items-center justify-between gap-2">
            <span class="text-fg-muted">Onboarded</span>
            <span class="font-mono text-xs text-fg">{{ data.subjectOnboarded ? "yes" : "already" }}</span>
          </li>
          <li class="flex items-center justify-between gap-2">
            <span class="text-fg-muted">Auto-applied</span>
            <span class="font-mono text-xs text-fg">{{ data.autoAppliedInvitations }}</span>
          </li>
        </ul>
        <RouterLink to="/" class="mt-6 inline-block text-sm text-accent hover:underline">Continue →</RouterLink>
      </template>
    </div>
  </div>
</template>
