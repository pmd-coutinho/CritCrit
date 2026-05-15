<script setup lang="ts">
import {
  DialogRoot,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogTitle,
  DialogDescription,
  DialogClose,
} from "reka-ui";

defineProps<{
  open: boolean;
  title: string;
  description?: string;
}>();
defineEmits<{ (e: "update:open", value: boolean): void }>();
</script>

<template>
  <DialogRoot :open="open" @update:open="$emit('update:open', $event)">
    <DialogPortal>
      <DialogOverlay class="fixed inset-0 z-40 bg-black/60 backdrop-blur-[2px]" />
      <DialogContent
        class="fixed left-1/2 top-1/2 z-50 w-[min(28rem,92vw)] -translate-x-1/2 -translate-y-1/2 rounded-lg border border-border bg-surface p-5 shadow-[var(--shadow-pop)]"
      >
        <div class="mb-4 flex items-start justify-between gap-4">
          <div>
            <DialogTitle class="text-base font-semibold tracking-tight text-fg">{{ title }}</DialogTitle>
            <DialogDescription v-if="description" class="mt-1 text-sm text-fg-muted">{{ description }}</DialogDescription>
          </div>
          <DialogClose
            class="-mr-1 -mt-1 inline-flex h-7 w-7 items-center justify-center rounded-md text-fg-subtle hover:bg-surface-hover hover:text-fg"
            aria-label="Close"
          >×</DialogClose>
        </div>
        <slot />
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
