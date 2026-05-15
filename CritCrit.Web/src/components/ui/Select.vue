<script setup lang="ts" generic="T extends string | number">
defineProps<{
  modelValue: T;
  options: ReadonlyArray<{ value: T; label: string }>;
  disabled?: boolean;
}>();
defineEmits<{ (e: "update:modelValue", value: T): void }>();
</script>

<template>
  <select
    :value="modelValue"
    :disabled="disabled"
    class="h-8 rounded-md border border-border bg-surface px-2 pr-7 text-sm text-fg focus:outline-none focus:border-accent focus:shadow-[0_0_0_3px_var(--accent-soft)] disabled:opacity-50"
    @change="$emit('update:modelValue', ($event.target as HTMLSelectElement).value as T)"
  >
    <option v-for="opt in options" :key="String(opt.value)" :value="opt.value">{{ opt.label }}</option>
  </select>
</template>
