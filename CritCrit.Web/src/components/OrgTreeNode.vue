<script setup lang="ts">
import { ref } from "vue";
import {
  DropdownMenuRoot,
  DropdownMenuTrigger,
  DropdownMenuPortal,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
} from "reka-ui";
import type { OrgNodeType, OrgTreeNodeResponse } from "@/api/generated";
import Badge from "@/components/ui/Badge.vue";
import MonoId from "@/components/ui/MonoId.vue";

const props = defineProps<{ node: OrgTreeNodeResponse; depth: number }>();
const emit = defineEmits<{
  (e: "addChild", node: OrgTreeNodeResponse): void;
  (e: "archive", node: OrgTreeNodeResponse): void;
  (e: "restore", node: OrgTreeNodeResponse): void;
  (e: "move", node: OrgTreeNodeResponse): void;
  (e: "hardDelete", node: OrgTreeNodeResponse): void;
}>();

const open = ref(true);

const childAllowed: Record<OrgNodeType, boolean> = {
  Brand: true,
  Country: true,
  Franchise: true,
  Store: true,
  Device: false,
};

const glyph = (t: OrgNodeType) =>
  t === "Brand" ? "■" : t === "Country" ? "▣" : t === "Franchise" ? "▤" : t === "Store" ? "▥" : "▪";
</script>

<template>
  <div>
    <div
      class="group flex items-center gap-2 rounded-md px-2 py-1.5 transition-colors hover:bg-surface-hover"
      :style="{ paddingLeft: `${depth * 18 + 8}px` }"
    >
      <button
        v-if="node.children.length"
        class="flex h-4 w-4 items-center justify-center text-xs text-fg-subtle hover:text-fg"
        @click="open = !open"
      >
        {{ open ? "▾" : "▸" }}
      </button>
      <span v-else class="w-4 text-center text-xs text-fg-subtle">·</span>

      <span class="font-mono text-fg-subtle">{{ glyph(node.type) }}</span>

      <span class="text-sm text-fg">{{ node.name }}</span>
      <span class="font-mono text-xs text-fg-subtle">{{ node.code }}</span>

      <Badge v-if="node.effectiveArchived" tone="warn">archived</Badge>

      <div class="ml-auto flex items-center gap-2">
        <span class="hidden md:inline-flex"><MonoId :value="node.id" :truncate="14" /></span>
        <button
          v-if="childAllowed[node.type]"
          class="rounded-xs border border-border px-1.5 py-0.5 text-xs text-fg-muted hover:border-accent hover:text-accent"
          @click.stop="emit('addChild', node)"
        >+ add child</button>

        <DropdownMenuRoot>
          <DropdownMenuTrigger
            class="inline-flex h-6 w-6 items-center justify-center rounded-xs text-fg-subtle hover:bg-surface-hover hover:text-fg focus:outline-none"
            :aria-label="`Actions for ${node.name}`"
          >⋯</DropdownMenuTrigger>
          <DropdownMenuPortal>
            <DropdownMenuContent
              class="z-50 min-w-[10rem] rounded-md border border-border bg-surface p-1 text-sm shadow-[var(--shadow-pop)]"
              :side-offset="4"
              align="end"
            >
              <DropdownMenuItem
                v-if="!node.archived"
                class="cursor-pointer rounded-xs px-2 py-1.5 text-fg hover:bg-surface-hover focus:bg-surface-hover focus:outline-none"
                @select="emit('archive', node)"
              >Archive…</DropdownMenuItem>
              <DropdownMenuItem
                v-else
                class="cursor-pointer rounded-xs px-2 py-1.5 text-fg hover:bg-surface-hover focus:bg-surface-hover focus:outline-none"
                @select="emit('restore', node)"
              >Restore</DropdownMenuItem>
              <DropdownMenuItem
                v-if="node.parentId"
                class="cursor-pointer rounded-xs px-2 py-1.5 text-fg hover:bg-surface-hover focus:bg-surface-hover focus:outline-none"
                @select="emit('move', node)"
              >Move…</DropdownMenuItem>
              <DropdownMenuSeparator class="my-1 h-px bg-border" />
              <DropdownMenuItem
                class="cursor-pointer rounded-xs px-2 py-1.5 text-danger hover:bg-danger-soft focus:bg-danger-soft focus:outline-none"
                @select="emit('hardDelete', node)"
              >Hard-delete…</DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenuPortal>
        </DropdownMenuRoot>
      </div>
    </div>

    <div v-if="open && node.children.length">
      <OrgTreeNode
        v-for="child in node.children"
        :key="child.id"
        :node="child"
        :depth="depth + 1"
        @add-child="emit('addChild', $event)"
        @archive="emit('archive', $event)"
        @restore="emit('restore', $event)"
        @move="emit('move', $event)"
        @hard-delete="emit('hardDelete', $event)"
      />
    </div>
  </div>
</template>
