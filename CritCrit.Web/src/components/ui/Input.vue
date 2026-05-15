<script setup lang="ts">
defineProps<{
  modelValue?: string;
  type?: string;
  placeholder?: string;
  disabled?: boolean;
  error?: string | null;
}>();
defineEmits<{ (e: "update:modelValue", value: string): void }>();
</script>

<template>
  <div class="flex flex-col gap-1">
    <input
      :value="modelValue"
      :type="type ?? 'text'"
      :placeholder="placeholder"
      :disabled="disabled"
      :aria-invalid="!!error"
      class="h-8 rounded-md border bg-surface px-2.5 text-sm text-fg placeholder:text-fg-subtle focus:outline-none focus:border-accent focus:shadow-[0_0_0_3px_var(--accent-soft)] disabled:opacity-50"
      :class="error ? 'border-danger' : 'border-border'"
      @input="$emit('update:modelValue', ($event.target as HTMLInputElement).value)"
    />
    <span v-if="error" class="text-xs text-danger">{{ error }}</span>
  </div>
</template>
