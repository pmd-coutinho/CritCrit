<script setup lang="ts">
import { computed, ref } from "vue";
import { useRoute } from "vue-router";
import { toast } from "vue-sonner";
import {
  useArchiveNode,
  useBrandTree,
  useCreateCountry,
  useCreateDevice,
  useCreateFranchise,
  useCreateStore,
  useHardDeleteNode,
  useMoveNode,
  useRestoreNode,
} from "@/api/queries";
import { errorMessage } from "@/api/errors";
import type { OrgNodeType, OrgTreeNodeResponse } from "@/api/generated";
import Button from "@/components/ui/Button.vue";
import Dialog from "@/components/ui/Dialog.vue";
import Input from "@/components/ui/Input.vue";
import Select from "@/components/ui/Select.vue";
import PageHeader from "@/components/ui/PageHeader.vue";
import EmptyState from "@/components/ui/EmptyState.vue";
import OrgTreeNode from "@/components/OrgTreeNode.vue";

const route = useRoute();
const brandId = computed(() => route.params.brandId as string);
const { data, isLoading, error, refetch } = useBrandTree(brandId);

const createCountry = useCreateCountry(brandId);
const createFranchise = useCreateFranchise(brandId);
const createStore = useCreateStore(brandId);
const createDevice = useCreateDevice(brandId);
const archiveNode = useArchiveNode(brandId);
const restoreNode = useRestoreNode(brandId);
const moveNode = useMoveNode(brandId);
const hardDeleteNode = useHardDeleteNode(brandId);

// ─── Create child ───
const dialogOpen = ref(false);
const dialogParent = ref<OrgTreeNodeResponse | null>(null);
const childType = ref<"Country" | "Franchise" | "Store" | "Device">("Country");
const code = ref("");
const name = ref("");
const timeZone = ref("UTC");
const deviceType = ref<"Pos" | "Kiosk" | "Display" | "Other">("Pos");
const submitting = ref(false);

const allowedChildren: Record<OrgNodeType, Array<"Country" | "Franchise" | "Store" | "Device">> = {
  Brand: ["Country", "Franchise", "Store"],
  Country: ["Franchise", "Store"],
  Franchise: ["Store"],
  Store: ["Device"],
  Device: [],
};

const allowed = computed(() => (dialogParent.value ? allowedChildren[dialogParent.value.type] : []));

function openCreate(parent: OrgTreeNodeResponse) {
  dialogParent.value = parent;
  childType.value = allowedChildren[parent.type][0] ?? "Country";
  code.value = "";
  name.value = "";
  timeZone.value = "UTC";
  deviceType.value = "Pos";
  dialogOpen.value = true;
}

async function submitCreate() {
  if (!dialogParent.value) return;
  submitting.value = true;
  try {
    const parentId = dialogParent.value.id;
    if (childType.value === "Country") {
      await createCountry.mutateAsync({ parentId, code: code.value, name: name.value });
    } else if (childType.value === "Franchise") {
      await createFranchise.mutateAsync({ parentId, code: code.value, name: name.value });
    } else if (childType.value === "Store") {
      await createStore.mutateAsync({ parentId, code: code.value, name: name.value, timeZone: timeZone.value });
    } else {
      await createDevice.mutateAsync({
        parentStoreId: parentId,
        serialNumber: code.value,
        name: name.value,
        deviceType: deviceType.value,
      });
    }
    toast.success(`${childType.value} created`);
    dialogOpen.value = false;
  } catch (err) {
    toast.error("Create failed", { description: errorMessage(err) });
  } finally {
    submitting.value = false;
  }
}

// ─── Archive ───
const archiveOpen = ref(false);
const archiveTarget = ref<OrgTreeNodeResponse | null>(null);
const archiveForce = ref(false);
const archiveReason = ref("");

function openArchive(node: OrgTreeNodeResponse) {
  archiveTarget.value = node;
  archiveForce.value = node.type === "Brand"; // brand requires force
  archiveReason.value = "";
  archiveOpen.value = true;
}

async function submitArchive() {
  if (!archiveTarget.value) return;
  submitting.value = true;
  try {
    await archiveNode.mutateAsync({
      nodeId: archiveTarget.value.id,
      body: { force: archiveForce.value, reason: archiveReason.value.trim() || null },
    });
    toast.success("Archived");
    archiveOpen.value = false;
  } catch (err) {
    toast.error("Archive failed", { description: errorMessage(err) });
  } finally {
    submitting.value = false;
  }
}

// ─── Restore ───
async function onRestore(node: OrgTreeNodeResponse) {
  if (!confirm(`Restore "${node.name}"?`)) return;
  try {
    await restoreNode.mutateAsync(node.id);
    toast.success("Restored");
  } catch (err) {
    toast.error("Restore failed", { description: errorMessage(err) });
  }
}

// ─── Move ───
const moveOpen = ref(false);
const moveTarget = ref<OrgTreeNodeResponse | null>(null);
const moveNewParentId = ref("");
const moveReason = ref("");

function openMove(node: OrgTreeNodeResponse) {
  moveTarget.value = node;
  moveNewParentId.value = "";
  moveReason.value = "";
  moveOpen.value = true;
}

async function submitMove() {
  if (!moveTarget.value) return;
  submitting.value = true;
  try {
    await moveNode.mutateAsync({
      nodeId: moveTarget.value.id,
      body: { newParentId: moveNewParentId.value.trim(), reason: moveReason.value.trim() },
    });
    toast.success("Moved");
    moveOpen.value = false;
  } catch (err) {
    toast.error("Move failed", { description: errorMessage(err) });
  } finally {
    submitting.value = false;
  }
}

// ─── Hard-delete ───
const deleteOpen = ref(false);
const deleteTarget = ref<OrgTreeNodeResponse | null>(null);
const deleteReason = ref("");
const deleteConfirm = ref("");

function openHardDelete(node: OrgTreeNodeResponse) {
  deleteTarget.value = node;
  deleteReason.value = "";
  deleteConfirm.value = "";
  deleteOpen.value = true;
}

async function submitHardDelete() {
  if (!deleteTarget.value) return;
  if (deleteConfirm.value.trim() !== deleteTarget.value.code) {
    toast.error("Type the node code exactly to confirm");
    return;
  }
  submitting.value = true;
  try {
    await hardDeleteNode.mutateAsync({
      nodeId: deleteTarget.value.id,
      body: { reason: deleteReason.value.trim() },
    });
    toast.success("Hard-deleted");
    deleteOpen.value = false;
  } catch (err) {
    toast.error("Hard-delete failed", { description: errorMessage(err) });
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <PageHeader title="Organization tree" subtitle="Brand hierarchy. Hover a node for actions.">
      <template #actions>
        <Button variant="ghost" @click="refetch">Refresh</Button>
      </template>
    </PageHeader>

    <div v-if="isLoading" class="space-y-2">
      <div class="skeleton h-8 w-1/3" />
      <div class="skeleton h-8 w-1/2 ml-6" />
      <div class="skeleton h-8 w-2/5 ml-12" />
    </div>

    <div v-else-if="error" class="rounded-md border border-danger bg-danger-soft p-3 text-sm text-fg">
      <p class="font-medium text-danger">Failed to load tree</p>
      <p class="mt-1 text-fg-muted">{{ errorMessage(error) }}</p>
    </div>

    <div v-else-if="data" class="rounded-lg border border-border bg-surface p-2">
      <OrgTreeNode
        :node="data"
        :depth="0"
        @add-child="openCreate"
        @archive="openArchive"
        @restore="onRestore"
        @move="openMove"
        @hard-delete="openHardDelete"
      />
    </div>

    <EmptyState v-else title="No data" />

    <!-- Create child dialog -->
    <Dialog
      v-model:open="dialogOpen"
      :title="`Add child to ${dialogParent?.name ?? ''}`"
      :description="dialogParent ? `Parent: ${dialogParent.type}` : ''"
    >
      <form class="flex flex-col gap-4" @submit.prevent="submitCreate">
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Type
          <Select v-model="childType" :options="allowed.map((v) => ({ value: v, label: v }))" />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          {{ childType === "Device" ? "Serial number" : "Code" }}
          <Input v-model="code" :placeholder="childType === 'Device' ? 'DV3PX...' : 'US, ACME-CO, ...'" />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Name
          <Input v-model="name" />
        </label>
        <label v-if="childType === 'Store'" class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Timezone
          <Input v-model="timeZone" placeholder="UTC" />
        </label>
        <label v-if="childType === 'Device'" class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Device type
          <Select
            v-model="deviceType"
            :options="[
              { value: 'Pos', label: 'POS' },
              { value: 'Kiosk', label: 'Kiosk' },
              { value: 'Display', label: 'Display' },
              { value: 'Other', label: 'Other' },
            ]"
          />
        </label>
        <div class="mt-2 flex justify-end gap-2">
          <Button variant="ghost" type="button" @click="dialogOpen = false">Cancel</Button>
          <Button variant="primary" type="submit" :loading="submitting">Create</Button>
        </div>
      </form>
    </Dialog>

    <!-- Archive dialog -->
    <Dialog
      v-model:open="archiveOpen"
      :title="`Archive ${archiveTarget?.name ?? ''}`"
      :description="archiveTarget?.type === 'Brand'
        ? 'Archiving a brand requires force + reason.'
        : 'Archives the node. If it has active children, force cascades them too.'"
    >
      <form class="flex flex-col gap-4" @submit.prevent="submitArchive">
        <label class="flex items-center gap-2 text-sm text-fg">
          <input
            type="checkbox"
            v-model="archiveForce"
            :disabled="archiveTarget?.type === 'Brand'"
            class="h-4 w-4 rounded-sm border-border bg-surface text-accent"
          />
          Force (cascades to descendants)
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Reason {{ archiveForce ? "(required)" : "(optional)" }}
          <Input v-model="archiveReason" placeholder="Why are you archiving this?" />
        </label>
        <div class="mt-2 flex justify-end gap-2">
          <Button variant="ghost" type="button" @click="archiveOpen = false">Cancel</Button>
          <Button variant="primary" type="submit" :loading="submitting">Archive</Button>
        </div>
      </form>
    </Dialog>

    <!-- Move dialog -->
    <Dialog
      v-model:open="moveOpen"
      :title="`Move ${moveTarget?.name ?? ''}`"
      description="Provide the new parent's ID. Must belong to the same brand."
    >
      <form class="flex flex-col gap-4" @submit.prevent="submitMove">
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          New parent ID
          <Input v-model="moveNewParentId" placeholder="co_... / fr_... / st_..." />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Reason
          <Input v-model="moveReason" />
        </label>
        <div class="mt-2 flex justify-end gap-2">
          <Button variant="ghost" type="button" @click="moveOpen = false">Cancel</Button>
          <Button variant="primary" type="submit" :loading="submitting">Move</Button>
        </div>
      </form>
    </Dialog>

    <!-- Hard-delete dialog -->
    <Dialog
      v-model:open="deleteOpen"
      :title="`Hard-delete ${deleteTarget?.name ?? ''}`"
      description="This is irreversible. Cascades to all descendants and revokes active grants."
    >
      <form class="flex flex-col gap-4" @submit.prevent="submitHardDelete">
        <div class="rounded-md border border-danger bg-danger-soft p-3 text-sm text-fg">
          <p class="font-medium text-danger">This cannot be undone.</p>
          <p class="mt-1 text-fg-muted">
            Type the node code <span class="font-mono text-fg">{{ deleteTarget?.code }}</span> to confirm.
          </p>
        </div>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Confirm code
          <Input v-model="deleteConfirm" />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Reason
          <Input v-model="deleteReason" placeholder="Required." />
        </label>
        <div class="mt-2 flex justify-end gap-2">
          <Button variant="ghost" type="button" @click="deleteOpen = false">Cancel</Button>
          <Button
            variant="danger"
            type="submit"
            :loading="submitting"
            :disabled="!deleteReason.trim() || deleteConfirm.trim() !== deleteTarget?.code"
          >Hard-delete</Button>
        </div>
      </form>
    </Dialog>
  </div>
</template>
