<script setup lang="ts">
import { computed, ref } from "vue";
import { useRoute } from "vue-router";
import { toast } from "vue-sonner";
import { z } from "zod";
import { useForm } from "vee-validate";
import { toTypedSchema } from "@vee-validate/zod";
import {
  useInvitations,
  useCreateInvitation,
  useCancelInvitation,
  useResendInvitation,
} from "@/api/queries";
import { errorMessage } from "@/api/errors";
import type { InvitationResponse } from "@/api/generated";
import Button from "@/components/ui/Button.vue";
import Dialog from "@/components/ui/Dialog.vue";
import Input from "@/components/ui/Input.vue";
import Select from "@/components/ui/Select.vue";
import Badge from "@/components/ui/Badge.vue";
import PageHeader from "@/components/ui/PageHeader.vue";
import EmptyState from "@/components/ui/EmptyState.vue";
import MonoId from "@/components/ui/MonoId.vue";

const route = useRoute();
const brandId = computed(() => route.params.brandId as string);

const { data: invitations, isLoading } = useInvitations(brandId);
const create = useCreateInvitation(brandId);
const cancel = useCancelInvitation(brandId);
const resend = useResendInvitation(brandId);

const createOpen = ref(false);

const schema = toTypedSchema(
  z.object({
    orgNodeId: z.string().trim().min(1, "Required"),
    email: z.string().email("Invalid email"),
    role: z.enum(["Viewer", "Member", "Admin", "Owner"]),
  }),
);

const { defineField, handleSubmit, errors, resetForm, isSubmitting } = useForm({
  validationSchema: schema,
  initialValues: { orgNodeId: brandId.value, email: "", role: "Member" },
});

const [orgNodeId] = defineField("orgNodeId");
const [email] = defineField("email");
const [roleField] = defineField("role");
const role = computed({
  get: () => roleField.value ?? "Member",
  set: (v: "Viewer" | "Member" | "Admin" | "Owner") => { roleField.value = v; },
});

const submit = handleSubmit(async (values) => {
  try {
    await create.mutateAsync(values);
    toast.success("Invitation sent", { description: values.email });
    createOpen.value = false;
    resetForm({ values: { orgNodeId: brandId.value, email: "", role: "Member" } });
  } catch (err) {
    toast.error("Create failed", { description: errorMessage(err) });
  }
});


function statusTone(s: InvitationResponse["status"]) {
  if (s === "Pending" || s === "Requested" || s === "Provisioning") return "accent" as const;
  if (s === "Accepted") return "success" as const;
  if (s === "Failed") return "danger" as const;
  return "neutral" as const;
}

async function onCancel(inv: InvitationResponse) {
  const reason = window.prompt(`Cancel invitation for ${inv.email}? Optional reason:`);
  if (reason === null) return;
  try {
    await cancel.mutateAsync({ invitationId: inv.id, reason: reason || null });
    toast.success("Invitation cancelled");
  } catch (err) {
    toast.error("Cancel failed", { description: errorMessage(err) });
  }
}

async function onResend(inv: InvitationResponse) {
  try {
    await resend.mutateAsync(inv.id);
    toast.success("Invitation resent");
  } catch (err) {
    toast.error("Resend failed", { description: errorMessage(err) });
  }
}

function fmtDate(iso: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString();
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <PageHeader title="Invitations" subtitle="Pending and recent invitations for this brand.">
      <template #actions>
        <Button variant="primary" @click="createOpen = true">+ Invite</Button>
      </template>
    </PageHeader>

    <div v-if="isLoading" class="space-y-1">
      <div v-for="i in 4" :key="i" class="skeleton h-9" />
    </div>

    <EmptyState
      v-else-if="!invitations?.length"
      title="No invitations yet"
      hint="Invite someone to start collaborating on this brand."
    >
      <Button variant="primary" @click="createOpen = true">+ Invite</Button>
    </EmptyState>

    <div v-else class="overflow-hidden rounded-lg border border-border bg-surface">
      <table class="w-full text-sm">
        <thead class="border-b border-border bg-bg/30 text-left text-xs uppercase tracking-wider text-fg-subtle">
          <tr>
            <th class="px-3 py-2 font-medium">Email</th>
            <th class="px-3 py-2 font-medium">Role</th>
            <th class="px-3 py-2 font-medium">Target</th>
            <th class="px-3 py-2 font-medium">Status</th>
            <th class="px-3 py-2 font-medium">Created</th>
            <th class="px-3 py-2 font-medium">Expires</th>
            <th class="px-3 py-2 font-medium text-right">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="inv in invitations"
            :key="inv.id"
            class="border-b border-border last:border-0 hover:bg-surface-hover"
          >
            <td class="px-3 py-2">{{ inv.email }}</td>
            <td class="px-3 py-2 text-fg-muted">{{ inv.role }}</td>
            <td class="px-3 py-2"><MonoId :value="inv.orgNodeId" :truncate="20" /></td>
            <td class="px-3 py-2"><Badge :tone="statusTone(inv.status)">{{ inv.status }}</Badge></td>
            <td class="px-3 py-2 text-xs text-fg-muted">{{ fmtDate(inv.createdAt) }}</td>
            <td class="px-3 py-2 text-xs text-fg-muted">{{ fmtDate(inv.expiresAt) }}</td>
            <td class="px-3 py-2">
              <div class="flex justify-end gap-1">
                <Button v-if="inv.status === 'Pending'" size="sm" variant="ghost" @click="onResend(inv)">Resend</Button>
                <Button v-if="['Pending','Requested','Provisioning'].includes(inv.status)" size="sm" variant="danger" @click="onCancel(inv)">Cancel</Button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <Dialog v-model:open="createOpen" title="New invitation" description="Invitee receives an email with an accept link.">
      <form class="flex flex-col gap-4" @submit.prevent="submit">
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Target org node ID
          <Input v-model="orgNodeId" :error="errors.orgNodeId" placeholder="br_... / co_... / fr_... / st_..." />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Email
          <Input v-model="email" :error="errors.email" type="email" placeholder="name@example.com" />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Role
          <Select
            v-model="role"
            :options="[
              { value: 'Viewer', label: 'Viewer' },
              { value: 'Member', label: 'Member' },
              { value: 'Admin', label: 'Admin' },
              { value: 'Owner', label: 'Owner' },
            ]"
          />
        </label>

        <div class="mt-2 flex justify-end gap-2">
          <Button variant="ghost" type="button" @click="createOpen = false">Cancel</Button>
          <Button variant="primary" type="submit" :loading="isSubmitting">Send invitation</Button>
        </div>
      </form>
    </Dialog>
  </div>
</template>
