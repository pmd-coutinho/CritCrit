<script setup lang="ts">
import { onMounted } from "vue";
import { useRouter } from "vue-router";
import { useAuthStore } from "@/stores/auth";

const auth = useAuthStore();
const router = useRouter();

onMounted(async () => {
  try {
    const returnTo = await auth.completeRedirect();
    await router.replace(returnTo || "/");
  } catch {
    await router.replace("/");
  }
});
</script>

<template>
  <div class="flex min-h-screen items-center justify-center text-sm text-fg-muted">
    Signing in…
  </div>
</template>
