<script setup lang="ts">
defineProps<{ title: string; subtitle?: string }>();
</script>

<template>
  <header class="flex items-end justify-between border-b border-border pb-4">
    <div>
      <h1 class="text-xl font-semibold tracking-tight text-fg">{{ title }}</h1>
      <p v-if="subtitle" class="mt-1 text-sm text-fg-muted">{{ subtitle }}</p>
    </div>
    <div class="flex items-center gap-2">
      <slot name="actions" />
    </div>
  </header>
</template>
