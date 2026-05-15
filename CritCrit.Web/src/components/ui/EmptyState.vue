<script setup lang="ts">
defineProps<{ title: string; hint?: string }>();
</script>

<template>
  <div class="flex flex-col items-center justify-center gap-3 rounded-lg border border-dashed border-border bg-surface py-12 text-center">
    <h3 class="text-sm font-medium text-fg">{{ title }}</h3>
    <p v-if="hint" class="max-w-sm text-sm text-fg-muted">{{ hint }}</p>
    <div class="mt-2 flex gap-2">
      <slot />
    </div>
  </div>
</template>
