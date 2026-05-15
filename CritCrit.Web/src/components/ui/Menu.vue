<script setup lang="ts">
import {
  DropdownMenuRoot,
  DropdownMenuTrigger,
  DropdownMenuPortal,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
} from "reka-ui";

defineProps<{ label?: string }>();
</script>

<template>
  <DropdownMenuRoot>
    <DropdownMenuTrigger
      class="inline-flex h-6 w-6 items-center justify-center rounded-xs text-fg-subtle hover:bg-surface-hover hover:text-fg focus:outline-none focus-visible:ring-2 focus-visible:ring-accent"
      :aria-label="label ?? 'Open menu'"
    >⋯</DropdownMenuTrigger>
    <DropdownMenuPortal>
      <DropdownMenuContent
        class="z-50 min-w-[10rem] rounded-md border border-border bg-surface p-1 text-sm shadow-[var(--shadow-pop)]"
        :side-offset="4"
      >
        <slot :Item="DropdownMenuItem" :Separator="DropdownMenuSeparator" />
      </DropdownMenuContent>
    </DropdownMenuPortal>
  </DropdownMenuRoot>
</template>
