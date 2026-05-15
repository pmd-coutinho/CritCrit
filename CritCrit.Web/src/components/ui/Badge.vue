<script setup lang="ts">
import { computed } from "vue";

type Tone = "neutral" | "accent" | "success" | "warn" | "danger";

const props = withDefaults(defineProps<{ tone?: Tone }>(), { tone: "neutral" });

const cls = computed(() => {
  const map: Record<Tone, string> = {
    neutral: "text-fg-muted border-border",
    accent: "text-accent border-accent/40",
    success: "text-success border-success/40",
    warn: "text-warn border-warn/40",
    danger: "text-danger border-danger/40",
  };
  return `inline-flex items-center gap-1 rounded-xs border px-1.5 py-0.5 text-xs font-medium uppercase tracking-wider ${map[props.tone]}`;
});
</script>

<template>
  <span :class="cls"><slot /></span>
</template>
