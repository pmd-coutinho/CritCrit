<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { toast } from "vue-sonner";
import { authenticatedFetch } from "@/api/client";
import {
  useInheritNodeAsset,
  useNodeAssets,
  useUnsetNodeAsset,
  useUploadNodeAsset,
} from "@/api/queries";
import type { AssetLookupResponse, OrgTreeNodeResponse } from "@/api/generated";
import { errorMessage } from "@/api/errors";
import Badge from "@/components/ui/Badge.vue";
import Button from "@/components/ui/Button.vue";
import Input from "@/components/ui/Input.vue";
import MonoId from "@/components/ui/MonoId.vue";

const props = defineProps<{ open: boolean; brandId: string; node: OrgTreeNodeResponse | null }>();
const emit = defineEmits<{ (e: "update:open", value: boolean): void }>();

const nodeId = computed(() => props.node?.id ?? "");
const { data: assets, isLoading, error } = useNodeAssets(() => props.brandId, nodeId);
const upload = useUploadNodeAsset(() => props.brandId, nodeId);
const unsetAsset = useUnsetNodeAsset(() => props.brandId, nodeId);
const inheritAsset = useInheritNodeAsset(() => props.brandId, nodeId);
const uploadPending = computed(() => upload.isPending.value);

const newKey = ref("kiosk.background-video");
const newFile = ref<File | null>(null);
const reason = ref("");
const previews = ref<Record<string, { url?: string; text?: string; loading?: boolean }>>({});

const grouped = computed(() => {
  const map = new Map<string, AssetLookupResponse[]>();
  for (const asset of assets.value ?? []) {
    const group = asset.group || "general";
    map.set(group, [...(map.get(group) ?? []), asset]);
  }
  return [...map.entries()].sort(([a], [b]) => a.localeCompare(b));
});

watch(() => props.open, (open) => {
  if (!open) clearPreviews();
});

function onFileChange(event: Event) {
  newFile.value = (event.target as HTMLInputElement).files?.[0] ?? null;
}

async function submitUpload(existing?: AssetLookupResponse) {
  if (!newFile.value) {
    toast.error("Choose a file first");
    return;
  }
  const key = (existing?.key ?? newKey.value).trim();
  try {
    await upload.mutateAsync({
      key,
      expectedVersion: existing?.valueSetVersion ?? 0,
      reason: reason.value.trim() || null,
      file: newFile.value,
    });
    toast.success("Asset uploaded", { description: key });
    newFile.value = null;
    reason.value = "";
    if (!existing) newKey.value = "";
  } catch (err) {
    toast.error("Upload failed", { description: errorMessage(err) });
  }
}

async function onUnset(asset: AssetLookupResponse) {
  try {
    await unsetAsset.mutateAsync({ key: asset.key, expectedVersion: asset.valueSetVersion, reason: reason.value.trim() || null });
    toast.success("Asset unset", { description: asset.key });
  } catch (err) {
    toast.error("Unset failed", { description: errorMessage(err) });
  }
}

async function onInherit(asset: AssetLookupResponse) {
  try {
    await inheritAsset.mutateAsync({ key: asset.key, expectedVersion: asset.valueSetVersion, reason: reason.value.trim() || null });
    toast.success("Asset now inherits", { description: asset.key });
  } catch (err) {
    toast.error("Inherit failed", { description: errorMessage(err) });
  }
}

async function loadPreview(asset: AssetLookupResponse) {
  if (!asset.contentUrl || !asset.file) return;
  previews.value[asset.key] = { loading: true };
  try {
    const res = await authenticatedFetch(asset.contentUrl);
    if (!res.ok) throw new Error(await res.text() || "Preview failed");
    const blob = await res.blob();
    if (asset.file.kind === "Markdown") {
      previews.value[asset.key] = { text: await blob.text() };
      return;
    }
    previews.value[asset.key] = { url: URL.createObjectURL(blob) };
  } catch (err) {
    previews.value[asset.key] = {};
    toast.error("Preview failed", { description: errorMessage(err) });
  }
}

async function download(asset: AssetLookupResponse) {
  if (!asset.contentUrl || !asset.file) return;
  const res = await authenticatedFetch(asset.contentUrl);
  if (!res.ok) {
    toast.error("Download failed", { description: await res.text() });
    return;
  }
  const url = URL.createObjectURL(await res.blob());
  const a = document.createElement("a");
  a.href = url;
  a.download = asset.file.fileName;
  a.click();
  URL.revokeObjectURL(url);
}

function clearPreviews() {
  for (const preview of Object.values(previews.value)) {
    if (preview.url) URL.revokeObjectURL(preview.url);
  }
  previews.value = {};
}

function fmtBytes(bytes: number) {
  if (bytes > 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  return `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

function tone(asset: AssetLookupResponse) {
  if (asset.state === "unset") return "warn";
  return asset.source === "Local" ? "success" : "neutral";
}
</script>

<template>
  <transition enter-active-class="transition-opacity duration-150" leave-active-class="transition-opacity duration-150" enter-from-class="opacity-0" leave-to-class="opacity-0">
    <div v-if="open" class="fixed inset-0 z-40 bg-black/40" @click="emit('update:open', false)" />
  </transition>

  <transition enter-active-class="transition-transform duration-150" leave-active-class="transition-transform duration-150" enter-from-class="translate-x-full" leave-to-class="translate-x-full">
    <aside v-if="open" class="fixed right-0 top-0 z-50 flex h-screen w-[min(42rem,95vw)] flex-col border-l border-border bg-bg shadow-[var(--shadow-pop)]">
      <header class="border-b border-border bg-[radial-gradient(circle_at_top_right,var(--accent-soft),transparent_40%)] px-5 py-4">
        <div class="flex items-start justify-between gap-4">
          <div>
            <p class="text-xs uppercase tracking-wider text-fg-subtle">Node assets</p>
            <p class="text-base font-semibold tracking-tight text-fg">{{ node?.name }}</p>
            <MonoId v-if="node" :value="node.id" :truncate="20" class="mt-0.5" />
          </div>
          <button class="inline-flex h-7 w-7 items-center justify-center rounded-md text-fg-subtle hover:bg-surface-hover hover:text-fg" @click="emit('update:open', false)">×</button>
        </div>
      </header>

      <section class="border-b border-border bg-surface/50 p-4">
        <div class="grid gap-3 md:grid-cols-[1fr_auto]">
          <Input v-model="newKey" placeholder="kiosk.background-video" />
          <input type="file" class="text-sm text-fg-muted file:mr-3 file:rounded-md file:border file:border-border file:bg-surface file:px-3 file:py-1.5 file:text-fg" @change="onFileChange" />
        </div>
        <div class="mt-3 flex items-center justify-between gap-3">
          <Input v-model="reason" placeholder="Reason (optional)" />
          <Button variant="primary" :loading="uploadPending" @click="submitUpload()">Upload</Button>
        </div>
        <p class="mt-2 text-xs text-fg-subtle">Keys are dot-separated groups. SVG is intentionally not accepted.</p>
      </section>

      <div v-if="isLoading" class="space-y-2 p-5">
        <div class="skeleton h-12" />
        <div class="skeleton h-12" />
      </div>
      <div v-else-if="error" class="m-5 rounded-md border border-danger bg-danger-soft p-3 text-sm">
        <p class="font-medium text-danger">Failed to load assets</p>
        <p class="mt-1 text-fg-muted">{{ errorMessage(error) }}</p>
      </div>
      <div v-else-if="!assets?.length" class="flex flex-1 items-center justify-center p-8 text-center text-sm text-fg-muted">
        No assets set at this node or its ancestors.
      </div>

      <div v-else class="flex-1 overflow-y-auto p-5">
        <section v-for="[group, rows] in grouped" :key="group" class="mb-6">
          <h3 class="mb-2 font-mono text-xs uppercase tracking-wider text-fg-subtle">{{ group }}</h3>
          <ul class="space-y-3">
            <li v-for="asset in rows" :key="asset.key" class="rounded-lg border border-border bg-surface p-3">
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <div class="flex flex-wrap items-center gap-2">
                    <span class="font-mono text-sm text-fg">{{ asset.key }}</span>
                    <Badge :tone="tone(asset)">{{ asset.state === "unset" ? "unset" : asset.source.toLowerCase() }}</Badge>
                    <Badge v-if="asset.file" tone="accent">{{ asset.file.kind }}</Badge>
                  </div>
                  <p class="mt-1 text-xs text-fg-muted">
                    <template v-if="asset.file">{{ asset.file.fileName }} · {{ fmtBytes(asset.file.length) }} · {{ asset.file.contentType }}</template>
                    <template v-else>Inheritance is blocked at this node.</template>
                  </p>
                </div>
                <div class="flex shrink-0 flex-wrap justify-end gap-1">
                  <Button v-if="asset.contentUrl" size="sm" variant="ghost" @click="loadPreview(asset)">Preview</Button>
                  <Button v-if="asset.contentUrl" size="sm" variant="ghost" @click="download(asset)">Download</Button>
                  <Button size="sm" variant="ghost" @click="onInherit(asset)">Inherit</Button>
                  <Button size="sm" variant="ghost" @click="onUnset(asset)">Unset</Button>
                </div>
              </div>

              <div v-if="previews[asset.key]?.url || previews[asset.key]?.text" class="mt-3 overflow-hidden rounded-md border border-border bg-bg p-2">
                <img v-if="asset.file?.kind === 'Image' && previews[asset.key]?.url" :src="previews[asset.key].url" class="max-h-64 rounded object-contain" />
                <video v-else-if="asset.file?.kind === 'Video' && previews[asset.key]?.url" :src="previews[asset.key].url" class="max-h-64 w-full rounded" controls />
                <iframe v-else-if="asset.file?.kind === 'Pdf' && previews[asset.key]?.url" :src="previews[asset.key].url" class="h-72 w-full rounded" />
                <pre v-else class="max-h-64 overflow-auto whitespace-pre-wrap text-xs text-fg">{{ previews[asset.key].text }}</pre>
              </div>
            </li>
          </ul>
        </section>
      </div>
    </aside>
  </transition>
</template>
