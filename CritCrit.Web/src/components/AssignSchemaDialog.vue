<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { toast } from "vue-sonner";
import {
  useConfigSchemas,
  useConfigSchemaVersions,
  useCreateAssignment,
} from "@/api/queries";
import { errorMessage } from "@/api/errors";
import Button from "@/components/ui/Button.vue";
import Dialog from "@/components/ui/Dialog.vue";
import Input from "@/components/ui/Input.vue";
import Select from "@/components/ui/Select.vue";

const props = defineProps<{
  open: boolean;
  brandId: string;
  nodeId: string;
  nodeName: string;
}>();
const emit = defineEmits<{
  (e: "update:open", value: boolean): void;
  (e: "assigned"): void;
}>();

const includeArchived = ref(false);
const { data: schemas, isLoading: schemasLoading } = useConfigSchemas(includeArchived);

const schemaCode = ref<string>("");
const schemaCodeRef = computed(() => schemaCode.value);
const { data: versions, isLoading: versionsLoading } = useConfigSchemaVersions(schemaCodeRef);

const schemaVersion = ref<number>(0);
const reason = ref("");
const submitting = ref(false);

const assign = useCreateAssignment(
  computed(() => props.brandId),
  computed(() => props.nodeId),
);

watch(
  () => props.open,
  (o) => {
    if (o) {
      schemaCode.value = "";
      schemaVersion.value = 0;
      reason.value = "";
    }
  },
);

watch(versions, (v) => {
  if (v?.length && !schemaVersion.value) {
    schemaVersion.value = v[v.length - 1].version;
  }
});

const schemaOptions = computed(() => {
  const items = (schemas.value ?? []).filter((s) => !s.archived && s.latestPublishedVersion);
  return [
    { value: "", label: "Pick a schema…" },
    ...items.map((s) => ({ value: s.code, label: `${s.code} — ${s.name}` })),
  ];
});

const versionOptions = computed(() =>
  (versions.value ?? []).map((v) => ({ value: v.version, label: `v${v.version}` })),
);

async function submit() {
  if (!schemaCode.value || !schemaVersion.value) {
    toast.error("Pick a schema and version");
    return;
  }
  submitting.value = true;
  try {
    await assign.mutateAsync({
      schemaCode: schemaCode.value,
      schemaVersion: schemaVersion.value,
      reason: reason.value.trim() || null,
    });
    toast.success("Schema assigned", { description: `${schemaCode.value} v${schemaVersion.value}` });
    emit("assigned");
    emit("update:open", false);
  } catch (err) {
    toast.error("Assign failed", { description: errorMessage(err) });
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <Dialog
    :open="open"
    :title="`Assign schema to ${nodeName}`"
    description="Assignment becomes the boundary at this node. Descendants inherit unless overridden."
    @update:open="(v) => emit('update:open', v)"
  >
    <form class="flex flex-col gap-4" @submit.prevent="submit">
      <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
        Schema
        <Select
          v-if="!schemasLoading"
          v-model="schemaCode"
          :options="schemaOptions"
        />
        <div v-else class="skeleton h-8" />
      </label>

      <label v-if="schemaCode" class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
        Version
        <Select
          v-if="!versionsLoading && versionOptions.length"
          v-model.number="schemaVersion"
          :options="versionOptions"
        />
        <p v-else-if="!versionsLoading" class="text-xs text-fg-subtle">No published versions yet.</p>
        <div v-else class="skeleton h-8" />
      </label>

      <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
        Reason
        <Input v-model="reason" placeholder="Optional context for the audit trail" />
      </label>

      <div class="mt-2 flex justify-end gap-2">
        <Button variant="ghost" type="button" @click="emit('update:open', false)">Cancel</Button>
        <Button
          variant="primary"
          type="submit"
          :loading="submitting"
          :disabled="!schemaCode || !schemaVersion"
        >Assign</Button>
      </div>
    </form>
  </Dialog>
</template>
