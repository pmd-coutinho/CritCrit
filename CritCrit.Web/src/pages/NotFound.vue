<script setup lang="ts">
import { RouterLink } from "vue-router";
</script>

<template>
  <div class="flex min-h-screen flex-col items-center justify-center gap-2">
    <p class="font-mono text-xs uppercase tracking-widest text-fg-subtle">404</p>
    <h1 class="text-xl font-semibold tracking-tight text-fg">Not found</h1>
    <RouterLink to="/" class="text-sm text-accent hover:underline">Back to start</RouterLink>
  </div>
</template>
