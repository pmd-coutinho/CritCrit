<script setup lang="ts">
import { RouterView } from "vue-router";
import { Toaster } from "vue-sonner";
</script>

<template>
  <RouterView />
  <Toaster
    position="bottom-right"
    theme="system"
    :toast-options="{
      style: {
        background: 'var(--surface)',
        color: 'var(--fg)',
        border: '1px solid var(--border)',
        fontFamily: 'var(--font-sans)',
        fontSize: 'var(--text-sm)',
      },
    }"
  />
</template>
