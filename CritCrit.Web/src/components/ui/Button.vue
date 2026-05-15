<script setup lang="ts">
import { computed } from "vue";

type Variant = "primary" | "secondary" | "ghost" | "danger";
type Size = "sm" | "md" | "lg";

const props = withDefaults(
  defineProps<{
    variant?: Variant;
    size?: Size;
    type?: "button" | "submit" | "reset";
    disabled?: boolean;
    loading?: boolean;
  }>(),
  { variant: "secondary", size: "md", type: "button", disabled: false, loading: false },
);

const classes = computed(() => {
  const base =
    "inline-flex items-center gap-1.5 rounded-md font-medium transition-colors select-none disabled:opacity-50 disabled:cursor-not-allowed border";
  const sizes: Record<Size, string> = {
    sm: "h-7 px-2.5 text-xs",
    md: "h-8 px-3 text-sm",
    lg: "h-9 px-4 text-sm",
  };
  const variants: Record<Variant, string> = {
    primary:
      "bg-accent text-accent-fg border-accent hover:bg-accent-strong hover:border-accent-strong",
    secondary:
      "bg-surface text-fg border-border hover:bg-surface-hover hover:border-border-strong",
    ghost:
      "bg-transparent text-fg-muted border-transparent hover:bg-surface-hover hover:text-fg",
    danger:
      "bg-transparent text-danger border-border hover:bg-danger-soft hover:border-danger",
  };
  return `${base} ${sizes[props.size]} ${variants[props.variant]}`;
});
</script>

<template>
  <button :type="type" :disabled="disabled || loading" :class="classes">
    <slot name="icon" />
    <slot />
    <span v-if="loading" class="ml-1 inline-block h-3 w-3 animate-spin rounded-full border border-current border-t-transparent" />
  </button>
</template>
