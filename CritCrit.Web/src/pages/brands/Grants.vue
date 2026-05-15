<script setup lang="ts">
import { computed, ref } from "vue";
import { useRoute } from "vue-router";
import { toast } from "vue-sonner";
import {
  useBrandGrants,
  useDowngradeOwner,
  useGrantOwner,
  useGrantRole,
  useRevokeGrant,
  useRevokeOwner,
  useSetGrantExpiration,
} from "@/api/queries";
import type { GrantListItem, OrgRole } from "@/api/generated";
import { errorMessage } from "@/api/errors";
import { useAuthStore } from "@/stores/auth";
import PageHeader from "@/components/ui/PageHeader.vue";
import EmptyState from "@/components/ui/EmptyState.vue";
import Badge from "@/components/ui/Badge.vue";
import MonoId from "@/components/ui/MonoId.vue";
import Button from "@/components/ui/Button.vue";
import Dialog from "@/components/ui/Dialog.vue";
import Input from "@/components/ui/Input.vue";
import Select from "@/components/ui/Select.vue";

const route = useRoute();
const brandId = computed(() => route.params.brandId as string);
const auth = useAuthStore();

const { data: grants, isLoading, error } = useBrandGrants(brandId);
const grantRole = useGrantRole(brandId);
const grantOwner = useGrantOwner(brandId);
const downgradeOwner = useDowngradeOwner(brandId);
const revokeOwner = useRevokeOwner(brandId);
const revokeGrant = useRevokeGrant(brandId);
const setGrantExpiration = useSetGrantExpiration(brandId);

const submitting = ref(false);

// ─── Create grant ───
const createOpen = ref(false);
const grantOrgNodeId = ref("");
const grantSubjectId = ref("");
const grantRoleValue = ref<OrgRole>("Member");
const grantExpires = ref("");

function openCreate() {
  grantOrgNodeId.value = brandId.value;
  grantSubjectId.value = "";
  grantRoleValue.value = "Member";
  grantExpires.value = "";
  createOpen.value = true;
}

async function submitGrant() {
  submitting.value = true;
  try {
    if (grantRoleValue.value === "Owner") {
      await grantOwner.mutateAsync({ subjectId: grantSubjectId.value.trim() });
    } else {
      await grantRole.mutateAsync({
        orgNodeId: grantOrgNodeId.value.trim(),
        subjectId: grantSubjectId.value.trim(),
        role: grantRoleValue.value,
        expiresAt: grantExpires.value ? new Date(grantExpires.value).toISOString() : null,
      });
    }
    toast.success("Grant created");
    createOpen.value = false;
  } catch (err) {
    toast.error("Grant failed", { description: errorMessage(err) });
  } finally {
    submitting.value = false;
  }
}

// ─── Change role (non-owner) ───
const roleOpen = ref(false);
const roleTarget = ref<GrantListItem | null>(null);
const newRole = ref<OrgRole>("Member");

function openChangeRole(g: GrantListItem) {
  roleTarget.value = g;
  newRole.value = g.role;
  roleOpen.value = true;
}

async function submitChangeRole() {
  if (!roleTarget.value) return;
  if (newRole.value === roleTarget.value.role) {
    toast.error("Pick a different role");
    return;
  }
  submitting.value = true;
  try {
    await grantRole.mutateAsync({
      orgNodeId: roleTarget.value.orgNodeId,
      subjectId: roleTarget.value.subjectId,
      role: newRole.value,
      expiresAt: roleTarget.value.expiresAt,
    });
    toast.success("Role updated");
    roleOpen.value = false;
  } catch (err) {
    toast.error("Update failed", { description: errorMessage(err) });
  } finally {
    submitting.value = false;
  }
}

// ─── Set expiration ───
const expOpen = ref(false);
const expTarget = ref<GrantListItem | null>(null);
const expValue = ref("");

function openSetExpiration(g: GrantListItem) {
  expTarget.value = g;
  expValue.value = g.expiresAt ? g.expiresAt.slice(0, 16) : "";
  expOpen.value = true;
}

async function submitExpiration() {
  if (!expTarget.value) return;
  submitting.value = true;
  try {
    await setGrantExpiration.mutateAsync({
      orgNodeId: expTarget.value.orgNodeId,
      subjectId: expTarget.value.subjectId,
      expiresAt: expValue.value ? new Date(expValue.value).toISOString() : null,
    });
    toast.success("Expiration updated");
    expOpen.value = false;
  } catch (err) {
    toast.error("Update failed", { description: errorMessage(err) });
  } finally {
    submitting.value = false;
  }
}

// ─── Revoke (non-owner) ───
async function onRevoke(g: GrantListItem) {
  const reason = window.prompt(
    `Revoke ${g.role} grant for ${g.subjectEmail} on ${g.orgNodeName}? Reason (optional):`,
  );
  if (reason === null) return;
  try {
    await revokeGrant.mutateAsync({
      orgNodeId: g.orgNodeId,
      subjectId: g.subjectId,
      reason: reason.trim() || null,
    });
    toast.success("Grant revoked");
  } catch (err) {
    toast.error("Revoke failed", { description: errorMessage(err) });
  }
}

// ─── Owner downgrade ───
const downgradeOpen = ref(false);
const downgradeTarget = ref<GrantListItem | null>(null);
const downgradeNewRole = ref<OrgRole>("Admin");
const downgradeReason = ref("");

function openDowngrade(g: GrantListItem) {
  downgradeTarget.value = g;
  downgradeNewRole.value = "Admin";
  downgradeReason.value = "";
  downgradeOpen.value = true;
}

async function submitDowngrade() {
  if (!downgradeTarget.value) return;
  submitting.value = true;
  try {
    await downgradeOwner.mutateAsync({
      subjectId: downgradeTarget.value.subjectId,
      body: { newRole: downgradeNewRole.value, reason: downgradeReason.value.trim() },
    });
    toast.success("Downgraded");
    downgradeOpen.value = false;
  } catch (err) {
    toast.error("Downgrade failed", { description: errorMessage(err) });
  } finally {
    submitting.value = false;
  }
}

// ─── Owner revoke ───
async function onRevokeOwner(g: GrantListItem) {
  const reason = window.prompt(`Revoke owner from ${g.subjectEmail}? Reason:`);
  if (!reason || !reason.trim()) return;
  try {
    await revokeOwner.mutateAsync({ subjectId: g.subjectId, body: { reason: reason.trim() } });
    toast.success("Revoked");
  } catch (err) {
    toast.error("Revoke failed", { description: errorMessage(err) });
  }
}

function roleTone(r: OrgRole) {
  if (r === "Owner") return "danger";
  if (r === "Admin") return "warn";
  if (r === "Member") return "accent";
  return "neutral";
}

function fmtDate(iso: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleDateString();
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <PageHeader title="Grants" subtitle="Active direct role grants on this brand and its descendants.">
      <template #actions>
        <Button variant="primary" @click="openCreate">+ Grant</Button>
      </template>
    </PageHeader>

    <div v-if="isLoading" class="space-y-1">
      <div v-for="i in 5" :key="i" class="skeleton h-9" />
    </div>

    <div v-else-if="error" class="rounded-md border border-danger bg-danger-soft p-3 text-sm">
      <p class="font-medium text-danger">Failed to load grants</p>
      <p class="mt-1 text-fg-muted">{{ errorMessage(error) }}</p>
    </div>

    <EmptyState
      v-else-if="!grants?.length"
      title="No grants yet"
      hint="Grant someone a role on a node here, or invite them via the Invitations tab."
    >
      <Button variant="primary" @click="openCreate">+ Grant</Button>
    </EmptyState>

    <div v-else class="overflow-hidden rounded-lg border border-border bg-surface">
      <table class="w-full text-sm">
        <thead class="border-b border-border bg-bg/30 text-left text-xs uppercase tracking-wider text-fg-subtle">
          <tr>
            <th class="px-3 py-2 font-medium">Subject</th>
            <th class="px-3 py-2 font-medium">Node</th>
            <th class="px-3 py-2 font-medium">Role</th>
            <th class="px-3 py-2 font-medium">Source</th>
            <th class="px-3 py-2 font-medium">Expires</th>
            <th class="px-3 py-2 font-medium text-right">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="g in grants" :key="g.id" class="border-b border-border last:border-0 hover:bg-surface-hover">
            <td class="px-3 py-2">
              <div class="flex flex-col">
                <span>{{ g.subjectDisplayName ?? g.subjectEmail }}</span>
                <span v-if="g.subjectDisplayName" class="text-xs text-fg-subtle">{{ g.subjectEmail }}</span>
              </div>
            </td>
            <td class="px-3 py-2">
              <div class="flex items-center gap-2">
                <span class="text-fg">{{ g.orgNodeName }}</span>
                <span class="font-mono text-xs text-fg-subtle">{{ g.orgNodeType }}</span>
              </div>
              <MonoId :value="g.orgNodeId" :truncate="20" />
            </td>
            <td class="px-3 py-2"><Badge :tone="roleTone(g.role)">{{ g.role }}</Badge></td>
            <td class="px-3 py-2 text-xs text-fg-muted">{{ g.source }}</td>
            <td class="px-3 py-2 text-xs text-fg-muted">{{ fmtDate(g.expiresAt) }}</td>
            <td class="px-3 py-2">
              <div class="flex justify-end gap-1">
                <template v-if="g.role === 'Owner'">
                  <Button v-if="auth.isSuperAdmin" size="sm" variant="ghost" @click="openDowngrade(g)">Downgrade</Button>
                  <Button v-if="auth.isSuperAdmin" size="sm" variant="danger" @click="onRevokeOwner(g)">Revoke</Button>
                </template>
                <template v-else>
                  <Button size="sm" variant="ghost" @click="openChangeRole(g)">Role…</Button>
                  <Button size="sm" variant="ghost" @click="openSetExpiration(g)">Expires…</Button>
                  <Button size="sm" variant="danger" @click="onRevoke(g)">Revoke</Button>
                </template>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Create grant dialog -->
    <Dialog v-model:open="createOpen" title="New grant" description="Grant a subject a role at an org node.">
      <form class="flex flex-col gap-4" @submit.prevent="submitGrant">
        <label v-if="grantRoleValue !== 'Owner'" class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Target org node ID
          <Input v-model="grantOrgNodeId" placeholder="br_... / co_... / fr_... / st_..." />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Subject ID
          <Input v-model="grantSubjectId" placeholder="subj_..." />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Role
          <Select
            v-model="grantRoleValue"
            :options="[
              { value: 'Viewer', label: 'Viewer' },
              { value: 'Member', label: 'Member' },
              { value: 'Admin', label: 'Admin' },
              ...(auth.isSuperAdmin ? [{ value: 'Owner' as const, label: 'Owner (brand root, SuperAdmin only)' }] : []),
            ]"
          />
        </label>
        <label v-if="grantRoleValue !== 'Owner'" class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Expires (optional)
          <Input v-model="grantExpires" type="datetime-local" />
        </label>
        <p v-if="grantRoleValue === 'Owner'" class="rounded-md border border-warn/30 bg-warn-soft p-2 text-xs text-fg-muted">
          Owner grants land at the brand root and bypass the node-target field.
        </p>
        <div class="mt-2 flex justify-end gap-2">
          <Button variant="ghost" type="button" @click="createOpen = false">Cancel</Button>
          <Button variant="primary" type="submit" :loading="submitting">Grant</Button>
        </div>
      </form>
    </Dialog>

    <!-- Change role dialog -->
    <Dialog
      v-model:open="roleOpen"
      :title="`Change role: ${roleTarget?.subjectEmail ?? ''}`"
      :description="`Currently ${roleTarget?.role} at ${roleTarget?.orgNodeName}.`"
    >
      <form class="flex flex-col gap-4" @submit.prevent="submitChangeRole">
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          New role
          <Select
            v-model="newRole"
            :options="[
              { value: 'Viewer', label: 'Viewer' },
              { value: 'Member', label: 'Member' },
              { value: 'Admin', label: 'Admin' },
            ]"
          />
        </label>
        <p class="text-xs text-fg-muted">
          Role changes preserve the existing expiration. Use the Expires… button to change that separately.
        </p>
        <div class="mt-2 flex justify-end gap-2">
          <Button variant="ghost" type="button" @click="roleOpen = false">Cancel</Button>
          <Button variant="primary" type="submit" :loading="submitting">Update role</Button>
        </div>
      </form>
    </Dialog>

    <!-- Set expiration dialog -->
    <Dialog
      v-model:open="expOpen"
      :title="`Expiration: ${expTarget?.subjectEmail ?? ''}`"
      description="Leave blank to clear the expiration."
    >
      <form class="flex flex-col gap-4" @submit.prevent="submitExpiration">
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Expires at
          <Input v-model="expValue" type="datetime-local" />
        </label>
        <div class="mt-2 flex justify-end gap-2">
          <Button variant="ghost" type="button" @click="expOpen = false">Cancel</Button>
          <Button variant="primary" type="submit" :loading="submitting">Update</Button>
        </div>
      </form>
    </Dialog>

    <!-- Downgrade owner dialog -->
    <Dialog
      v-model:open="downgradeOpen"
      :title="`Downgrade owner: ${downgradeTarget?.subjectEmail ?? ''}`"
      description="Drops the brand-root owner grant to a lesser role."
    >
      <form class="flex flex-col gap-4" @submit.prevent="submitDowngrade">
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          New role
          <Select
            v-model="downgradeNewRole"
            :options="[
              { value: 'Viewer', label: 'Viewer' },
              { value: 'Member', label: 'Member' },
              { value: 'Admin', label: 'Admin' },
            ]"
          />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Reason
          <Input v-model="downgradeReason" placeholder="Required" />
        </label>
        <div class="mt-2 flex justify-end gap-2">
          <Button variant="ghost" type="button" @click="downgradeOpen = false">Cancel</Button>
          <Button
            variant="primary"
            type="submit"
            :loading="submitting"
            :disabled="!downgradeReason.trim()"
          >Downgrade</Button>
        </div>
      </form>
    </Dialog>
  </div>
</template>
