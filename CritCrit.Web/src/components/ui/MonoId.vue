<script setup lang="ts">
import { ref } from "vue";
import { toast } from "vue-sonner";

const props = defineProps<{ value: string; truncate?: number }>();

const hovered = ref(false);

async function copy() {
  try {
    await navigator.clipboard.writeText(props.value);
    toast.success("Copied", { description: props.value });
  } catch {
    toast.error("Copy failed");
  }
}

function display() {
  if (!props.truncate || props.value.length <= props.truncate) return props.value;
  return props.value.slice(0, props.truncate) + "…";
}
</script>

<template>
  <span
    class="group inline-flex items-center gap-1 font-mono text-xs text-fg-muted"
    @mouseenter="hovered = true"
    @mouseleave="hovered = false"
  >
    <span>{{ display() }}</span>
    <button
      v-show="hovered"
      class="rounded-xs px-1 text-fg-subtle hover:bg-surface-hover hover:text-fg"
      :title="value"
      @click.stop="copy"
    >⧉</button>
  </span>
</template>
