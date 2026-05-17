<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { toast } from "vue-sonner";
import {
  useArchiveAssignment,
  useConfigSchemaVersions,
  useNodeAssignments,
  useNodeConfigMetadata,
  useNodeConfigSchemas,
  usePatchNodeConfig,
  useUpgradeAssignment,
} from "@/api/queries";
import type {
  ConfigKeyDefinition,
  ConfigLookupValueMetadata,
  ConfigPatchKind,
  ConfigValueType,
  OrgTreeNodeResponse,
} from "@/api/generated";
import { errorMessage } from "@/api/errors";
import Badge from "@/components/ui/Badge.vue";
import Button from "@/components/ui/Button.vue";
import Input from "@/components/ui/Input.vue";
import MonoId from "@/components/ui/MonoId.vue";
import Select from "@/components/ui/Select.vue";
import AssignSchemaDialog from "@/components/AssignSchemaDialog.vue";

const props = defineProps<{
  open: boolean;
  brandId: string;
  node: OrgTreeNodeResponse | null;
}>();
const emit = defineEmits<{ (e: "update:open", value: boolean): void }>();

const brandIdRef = computed(() => props.brandId);
const nodeIdRef = computed(() => props.node?.id ?? "");

const { data: schemas, isLoading: schemasLoading, refetch: refetchSchemas } =
  useNodeConfigSchemas(brandIdRef, nodeIdRef);
const { data: assignments, refetch: refetchAssignments } =
  useNodeAssignments(brandIdRef, nodeIdRef);

const selectedSchemaCode = ref<string>("");

watch(schemas, (list) => {
  if (!list?.length) {
    selectedSchemaCode.value = "";
    return;
  }
  if (!list.find((s) => s.schemaCode === selectedSchemaCode.value)) {
    selectedSchemaCode.value = list[0].schemaCode;
  }
});

const selectedSummary = computed(() =>
  schemas.value?.find((s) => s.schemaCode === selectedSchemaCode.value) ?? null,
);

// Effective values metadata for current schema bag.
const metaPath = computed(() => selectedSummary.value?.schemaCode ?? "");
const { data: metadata, refetch: refetchMetadata } = useNodeConfigMetadata(
  brandIdRef,
  nodeIdRef,
  metaPath,
);

// Schema versions list — pick definition for current assignment version.
const versionsCode = computed(() => selectedSummary.value?.schemaCode ?? "");
const { data: versions } = useConfigSchemaVersions(versionsCode);

const currentDefinitionKeys = computed<ConfigKeyDefinition[]>(() => {
  const v = versions.value?.find((x) => x.version === selectedSummary.value?.schemaVersion);
  return v?.definition.keys ?? [];
});

// Whether the assignment for this schema is rooted at the current node — only
// then are archive/upgrade actions applicable (assignment lives there).
const rootedHere = computed(() =>
  selectedSummary.value?.assignmentRootOrgNodePublicId === props.node?.id,
);

// Find the matching assignment doc (loads only when rootedHere).
const rootedAssignment = computed(() => {
  if (!rootedHere.value || !selectedSummary.value) return null;
  return (
    assignments.value?.find(
      (a) => a.schemaCode === selectedSummary.value!.schemaCode && !a.archived,
    ) ?? null
  );
});

// ─── editing one key inline ───

const editingKey = ref<string>("");
const draftJson = ref<string>("");
const draftBool = ref(false);
const submitting = ref(false);

const patch = usePatchNodeConfig(brandIdRef, nodeIdRef);
const archiveAssignment = useArchiveAssignment(brandIdRef, nodeIdRef);
const upgradeAssignment = useUpgradeAssignment(brandIdRef, nodeIdRef);

function startEdit(key: ConfigKeyDefinition, current: ConfigLookupValueMetadata | undefined) {
  editingKey.value = key.code;
  if (key.valueType === "Boolean") {
    draftBool.value =
      current?.state === "set" && typeof current.value === "boolean" ? current.value : false;
    draftJson.value = JSON.stringify(draftBool.value);
  } else if (key.valueType === "EncryptedString") {
    draftJson.value = "";
  } else if (current?.state === "set" || current?.state === "default") {
    draftJson.value =
      typeof current.value === "string"
        ? current.value
        : JSON.stringify(current.value, null, 0);
  } else {
    draftJson.value = "";
  }
}

function cancelEdit() {
  editingKey.value = "";
  draftJson.value = "";
}

function expectedVersion(): number {
  return selectedSummary.value?.valueSetVersion ?? 0;
}

async function applyPatch(keyCode: string, operation: ConfigPatchKind, jsonValue: string | null) {
  if (!selectedSummary.value) return;
  submitting.value = true;
  try {
    await patch.mutateAsync({
      schemaCode: selectedSummary.value.schemaCode,
      body: {
        expectedVersion: expectedVersion(),
        operations: [{ keyCode, operation, jsonValue }],
        reason: null,
      },
    });
    toast.success(`${keyCode} ${operation === "Set" ? "updated" : operation === "Unset" ? "cleared" : "set to inherit"}`);
    editingKey.value = "";
    await refetchSchemas();
    await refetchMetadata();
  } catch (err) {
    toast.error("Patch failed", { description: errorMessage(err) });
  } finally {
    submitting.value = false;
  }
}

function buildJsonValue(key: ConfigKeyDefinition): string | null {
  if (key.valueType === "Boolean") return JSON.stringify(draftBool.value);
  if (key.valueType === "String" || key.valueType === "EncryptedString") {
    return JSON.stringify(draftJson.value);
  }
  if (key.valueType === "Integer") {
    const n = parseInt(draftJson.value, 10);
    if (Number.isNaN(n)) throw new Error("Not an integer.");
    return JSON.stringify(n);
  }
  if (key.valueType === "Decimal") {
    const n = parseFloat(draftJson.value);
    if (Number.isNaN(n)) throw new Error("Not a number.");
    return JSON.stringify(n);
  }
  // JsonObject / JsonArray — raw JSON
  try {
    JSON.parse(draftJson.value);
  } catch {
    throw new Error("Invalid JSON.");
  }
  return draftJson.value;
}

async function onSave(key: ConfigKeyDefinition) {
  try {
    const json = buildJsonValue(key);
    await applyPatch(key.code, "Set", json);
  } catch (err) {
    toast.error("Invalid value", { description: errorMessage(err) });
  }
}

const showAssign = ref(false);

async function onArchiveAssignment() {
  const a = rootedAssignment.value;
  if (!a) return;
  if (!confirm(`Archive assignment for ${a.schemaCode}? Descendants stop seeing this schema.`)) return;
  try {
    await archiveAssignment.mutateAsync({
      assignmentId: a.id,
      body: { expectedVersion: a.version, reason: null },
    });
    toast.success("Assignment archived");
    await refetchAssignments();
    await refetchSchemas();
  } catch (err) {
    toast.error("Archive failed", { description: errorMessage(err) });
  }
}

// ─── upgrade flow ───

const upgradeOpen = ref(false);
const upgradeTarget = ref<number>(0);

const upgradeCandidates = computed(() => {
  const a = rootedAssignment.value;
  if (!a || !versions.value) return [];
  return versions.value
    .filter((v) => v.version > a.schemaVersion)
    .map((v) => ({ value: v.version, label: `v${v.version}` }));
});

function openUpgrade() {
  upgradeTarget.value = upgradeCandidates.value[0]?.value ?? 0;
  upgradeOpen.value = true;
}

async function submitUpgrade() {
  const a = rootedAssignment.value;
  if (!a || !upgradeTarget.value) return;
  try {
    await upgradeAssignment.mutateAsync({
      assignmentId: a.id,
      body: {
        expectedVersion: a.version,
        targetSchemaVersion: upgradeTarget.value,
        reason: null,
      },
    });
    toast.success(`Upgraded to v${upgradeTarget.value}`);
    upgradeOpen.value = false;
    await refetchAssignments();
    await refetchSchemas();
    await refetchMetadata();
  } catch (err) {
    toast.error("Upgrade failed", { description: errorMessage(err) });
  }
}

function stateTone(state: string): "neutral" | "accent" | "success" | "warn" {
  if (state === "set") return "success";
  if (state === "default") return "neutral";
  if (state === "unset") return "warn";
  return "accent";
}

function valuePreview(meta: ConfigLookupValueMetadata | undefined, valueType: ConfigValueType): string {
  if (!meta) return "—";
  if (meta.encrypted) return meta.maskedValue ?? "********";
  if (meta.state === "unset") return "(unset, no inherit)";
  if (meta.state === "missing") return "—";
  if (meta.value === undefined || meta.value === null) return "null";
  if (valueType === "String" || valueType === "EncryptedString") return String(meta.value);
  if (valueType === "Boolean" || valueType === "Integer" || valueType === "Decimal") {
    return String(meta.value);
  }
  try {
    return JSON.stringify(meta.value);
  } catch {
    return "—";
  }
}

function metaFor(code: string): ConfigLookupValueMetadata | undefined {
  return metadata.value?.values?.[code];
}
</script>

<template>
  <transition
    enter-active-class="transition-opacity duration-100"
    leave-active-class="transition-opacity duration-100"
    enter-from-class="opacity-0"
    leave-to-class="opacity-0"
  >
    <div v-if="open" class="fixed inset-0 z-40 bg-black/40" @click="emit('update:open', false)" />
  </transition>

  <transition
    enter-active-class="transition-transform duration-150"
    leave-active-class="transition-transform duration-150"
    enter-from-class="translate-x-full"
    leave-to-class="translate-x-full"
  >
    <aside
      v-if="open"
      class="fixed right-0 top-0 z-50 flex h-screen w-[min(40rem,95vw)] flex-col border-l border-border bg-bg shadow-[var(--shadow-pop)]"
    >
      <header class="flex items-start justify-between gap-4 border-b border-border px-5 py-4">
        <div>
          <p class="text-xs uppercase tracking-wider text-fg-subtle">Node config</p>
          <p class="text-base font-semibold tracking-tight text-fg">{{ node?.name }}</p>
          <MonoId v-if="node" :value="node.id" :truncate="20" class="mt-0.5" />
        </div>
        <div class="flex items-center gap-2">
          <Button variant="primary" size="sm" @click="showAssign = true">+ Assign</Button>
          <button
            class="inline-flex h-7 w-7 items-center justify-center rounded-md text-fg-subtle hover:bg-surface-hover hover:text-fg"
            aria-label="Close"
            @click="emit('update:open', false)"
          >×</button>
        </div>
      </header>

      <div v-if="schemasLoading" class="space-y-2 p-5">
        <div class="skeleton h-8 w-1/3" />
        <div class="skeleton h-12" />
        <div class="skeleton h-12" />
      </div>

      <div v-else-if="!schemas?.length" class="flex flex-1 flex-col items-center justify-center gap-3 p-8 text-center">
        <p class="text-sm text-fg-muted">No schemas assigned at or above this node.</p>
        <Button variant="primary" size="sm" @click="showAssign = true">+ Assign a schema</Button>
      </div>

      <template v-else>
        <nav class="flex gap-1 overflow-x-auto border-b border-border bg-surface/40 px-3">
          <button
            v-for="s in schemas"
            :key="s.schemaCode"
            class="border-b-2 px-3 py-2 text-xs font-medium tracking-wider transition-colors"
            :class="
              selectedSchemaCode === s.schemaCode
                ? 'border-accent text-fg'
                : 'border-transparent text-fg-muted hover:text-fg'
            "
            @click="selectedSchemaCode = s.schemaCode"
          >
            {{ s.schemaCode }}
            <span class="ml-1 font-mono text-fg-subtle">v{{ s.schemaVersion }}</span>
          </button>
        </nav>

        <div v-if="selectedSummary" class="flex flex-1 flex-col overflow-y-auto">
          <div class="flex items-center justify-between gap-2 border-b border-border px-5 py-3">
            <div class="flex flex-wrap items-center gap-2 text-xs text-fg-muted">
              <Badge tone="accent">root: {{ selectedSummary.assignmentRootOrgNodePublicId }}</Badge>
              <Badge v-if="rootedHere" tone="success">assigned here</Badge>
              <Badge v-else tone="neutral">inherited</Badge>
            </div>
            <div class="flex items-center gap-2">
              <Button
                v-if="rootedHere && upgradeCandidates.length"
                size="sm"
                variant="secondary"
                @click="openUpgrade"
              >Upgrade…</Button>
              <Button
                v-if="rootedHere"
                size="sm"
                variant="danger"
                @click="onArchiveAssignment"
              >Archive assignment</Button>
            </div>
          </div>

          <ul class="divide-y divide-border">
            <li
              v-for="key in currentDefinitionKeys"
              :key="key.code"
              class="px-5 py-3"
            >
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0 flex-1">
                  <div class="flex items-center gap-2">
                    <span class="font-mono text-sm text-fg">{{ key.code }}</span>
                    <Badge tone="neutral">{{ key.valueType }}</Badge>
                    <Badge :tone="stateTone(metaFor(key.code)?.state ?? 'missing')">
                      {{ metaFor(key.code)?.state ?? "missing" }}
                    </Badge>
                  </div>
                  <p class="mt-0.5 text-xs text-fg-muted">{{ key.name }}<span v-if="key.description"> — {{ key.description }}</span></p>
                  <p class="mt-1 truncate font-mono text-xs text-fg-subtle">
                    {{ valuePreview(metaFor(key.code), key.valueType) }}
                    <span v-if="metaFor(key.code)?.source" class="ml-2 text-fg-muted">
                      ← {{ metaFor(key.code)?.source }}
                    </span>
                  </p>
                </div>
                <div v-if="editingKey !== key.code" class="flex items-center gap-1">
                  <Button size="sm" variant="ghost" @click="startEdit(key, metaFor(key.code))">Edit</Button>
                </div>
              </div>

              <div v-if="editingKey === key.code" class="mt-3 flex flex-col gap-2 rounded-md border border-border bg-surface p-3">
                <div v-if="key.valueType === 'Boolean'" class="flex gap-2">
                  <button
                    type="button"
                    class="h-8 rounded-md border px-3 text-sm transition-colors"
                    :class="draftBool ? 'border-accent bg-accent/15 text-fg' : 'border-border text-fg-muted hover:text-fg'"
                    @click="draftBool = true"
                  >true</button>
                  <button
                    type="button"
                    class="h-8 rounded-md border px-3 text-sm transition-colors"
                    :class="!draftBool ? 'border-accent bg-accent/15 text-fg' : 'border-border text-fg-muted hover:text-fg'"
                    @click="draftBool = false"
                  >false</button>
                </div>
                <Input
                  v-else-if="key.valueType === 'Integer' || key.valueType === 'Decimal'"
                  v-model="draftJson"
                  type="number"
                  :step="key.valueType === 'Decimal' ? '0.01' : '1'"
                />
                <Input
                  v-else-if="key.valueType === 'String' || key.valueType === 'EncryptedString'"
                  v-model="draftJson"
                  :placeholder="key.valueType === 'EncryptedString' ? 'New secret (write-only)' : 'Value'"
                />
                <textarea
                  v-else
                  v-model="draftJson"
                  rows="6"
                  placeholder='{"k":"v"}'
                  class="rounded-md border border-border bg-bg p-2 font-mono text-xs text-fg focus:outline-none focus:border-accent"
                />
                <div class="flex items-center justify-between gap-2 pt-1">
                  <div class="flex gap-2">
                    <Button size="sm" variant="ghost" @click="applyPatch(key.code, 'Inherit', null)" :loading="submitting">Inherit</Button>
                    <Button size="sm" variant="ghost" @click="applyPatch(key.code, 'Unset', null)" :loading="submitting">Unset</Button>
                  </div>
                  <div class="flex gap-2">
                    <Button size="sm" variant="ghost" @click="cancelEdit">Cancel</Button>
                    <Button size="sm" variant="primary" @click="onSave(key)" :loading="submitting">Save</Button>
                  </div>
                </div>
              </div>
            </li>
          </ul>
        </div>
      </template>
    </aside>
  </transition>

  <AssignSchemaDialog
    v-if="node"
    :open="showAssign"
    :brand-id="brandId"
    :node-id="node.id"
    :node-name="node.name"
    @update:open="showAssign = $event"
    @assigned="refetchSchemas(); refetchAssignments()"
  />

  <!-- Upgrade dialog (inline, simple) -->
  <transition
    enter-active-class="transition-opacity duration-100"
    leave-active-class="transition-opacity duration-100"
    enter-from-class="opacity-0"
    leave-to-class="opacity-0"
  >
    <div v-if="upgradeOpen" class="fixed inset-0 z-[60] flex items-center justify-center bg-black/60 backdrop-blur-[2px]">
      <div class="w-[min(26rem,92vw)] rounded-lg border border-border bg-surface p-5 shadow-[var(--shadow-pop)]">
        <p class="text-base font-semibold text-fg">Upgrade assignment</p>
        <p class="mt-1 text-sm text-fg-muted">
          Move {{ rootedAssignment?.schemaCode }} from v{{ rootedAssignment?.schemaVersion }} to a newer published version. Local values for keys that survive are preserved; incompatible keys are dropped on next patch.
        </p>
        <label class="mt-4 flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Target version
          <Select v-model.number="upgradeTarget" :options="upgradeCandidates" />
        </label>
        <div class="mt-5 flex justify-end gap-2">
          <Button variant="ghost" @click="upgradeOpen = false">Cancel</Button>
          <Button variant="primary" :disabled="!upgradeTarget" @click="submitUpgrade">Upgrade</Button>
        </div>
      </div>
    </div>
  </transition>
</template>
