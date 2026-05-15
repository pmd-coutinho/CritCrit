<script setup lang="ts">
import { computed, ref } from "vue";
import { toast } from "vue-sonner";
import { z } from "zod";
import { useForm } from "vee-validate";
import { toTypedSchema } from "@vee-validate/zod";
import {
  useSubjects,
  useCreateSubject,
  useDeactivateSubject,
  useReactivateSubject,
  useRelinkSubject,
} from "@/api/queries";
import type { SubjectListItem } from "@/api/generated";
import { errorMessage } from "@/api/errors";
import PageHeader from "@/components/ui/PageHeader.vue";
import EmptyState from "@/components/ui/EmptyState.vue";
import Badge from "@/components/ui/Badge.vue";
import Button from "@/components/ui/Button.vue";
import Dialog from "@/components/ui/Dialog.vue";
import Input from "@/components/ui/Input.vue";
import Select from "@/components/ui/Select.vue";
import MonoId from "@/components/ui/MonoId.vue";

const filter = ref("");
const onboardedFilter = ref<"all" | "yes" | "no">("all");
const onboardedQuery = computed<boolean | null>(() => {
  if (onboardedFilter.value === "yes") return true;
  if (onboardedFilter.value === "no") return false;
  return null;
});

const { data: subjects, isLoading, error } = useSubjects(filter, onboardedQuery, 100);
const createSubject = useCreateSubject();
const deactivateSubject = useDeactivateSubject();
const reactivateSubject = useReactivateSubject();
const relinkSubject = useRelinkSubject();

// ─── Deactivate / Reactivate ───
async function onDeactivate(s: SubjectListItem) {
  const reason = window.prompt(
    `Deactivate ${s.email}? This revokes EVERY active grant they hold across every brand. Reason (optional):`,
  );
  if (reason === null) return;
  try {
    await deactivateSubject.mutateAsync({
      subjectId: s.id,
      body: { reason: reason.trim() || null },
    });
    toast.success("Subject deactivated", { description: s.email });
  } catch (err) {
    toast.error("Deactivate failed", { description: errorMessage(err) });
  }
}

async function onReactivate(s: SubjectListItem) {
  const reason = window.prompt(
    `Reactivate ${s.email}? Note: prior grants stay revoked. You'll need to re-grant or re-invite. Reason (optional):`,
  );
  if (reason === null) return;
  try {
    await reactivateSubject.mutateAsync({
      subjectId: s.id,
      body: { reason: reason.trim() || null },
    });
    toast.success("Subject reactivated");
  } catch (err) {
    toast.error("Reactivate failed", { description: errorMessage(err) });
  }
}

// ─── Relink (Keycloak user re-created) ───
const relinkOpen = ref(false);
const relinkTarget = ref<SubjectListItem | null>(null);
const relinkProvider = ref("keycloak");
const relinkProviderTenant = ref("api");
const relinkOldExternalId = ref("");
const relinkNewExternalId = ref("");
const relinkReason = ref("");

function openRelink(s: SubjectListItem) {
  relinkTarget.value = s;
  relinkProvider.value = "keycloak";
  relinkProviderTenant.value = "api";
  relinkOldExternalId.value = "";
  relinkNewExternalId.value = "";
  relinkReason.value = "";
  relinkOpen.value = true;
}

async function submitRelink() {
  if (!relinkTarget.value) return;
  try {
    await relinkSubject.mutateAsync({
      subjectId: relinkTarget.value.id,
      body: {
        provider: relinkProvider.value.trim(),
        providerTenant: relinkProviderTenant.value.trim(),
        oldExternalId: relinkOldExternalId.value.trim(),
        newExternalId: relinkNewExternalId.value.trim(),
        reason: relinkReason.value.trim() || null,
      },
    });
    toast.success("Identity relinked");
    relinkOpen.value = false;
  } catch (err) {
    toast.error("Relink failed", { description: errorMessage(err) });
  }
}

const createOpen = ref(false);

const schema = toTypedSchema(
  z.object({
    email: z.string().email("Invalid email"),
    displayName: z.string().optional(),
    provider: z.string().trim().min(1, "Required"),
    providerTenant: z.string().trim().min(1, "Required"),
    externalId: z.string().trim().min(1, "Required"),
  }),
);

const { defineField, handleSubmit, errors, resetForm, isSubmitting } = useForm({
  validationSchema: schema,
  initialValues: { email: "", displayName: "", provider: "keycloak", providerTenant: "api", externalId: "" },
});

const [email] = defineField("email");
const [displayName] = defineField("displayName");
const [provider] = defineField("provider");
const [providerTenant] = defineField("providerTenant");
const [externalId] = defineField("externalId");

const submit = handleSubmit(async (values) => {
  try {
    await createSubject.mutateAsync({
      email: values.email,
      displayName: values.displayName?.trim() || null,
      provider: values.provider,
      providerTenant: values.providerTenant,
      externalId: values.externalId,
    });
    toast.success("Subject created", { description: values.email });
    createOpen.value = false;
    resetForm({
      values: { email: "", displayName: "", provider: "keycloak", providerTenant: "api", externalId: "" },
    });
  } catch (err) {
    toast.error("Create failed", { description: errorMessage(err) });
  }
});

function fmtDate(iso: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleDateString();
}

function kindTone(k: SubjectListItem["kind"]) {
  return k === "User" ? "neutral" : k === "Service" ? "accent" : "warn";
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <PageHeader title="Subjects" subtitle="Every identity CritCrit knows about. SuperAdmin only.">
      <template #actions>
        <Button variant="primary" @click="createOpen = true">+ Subject</Button>
      </template>
    </PageHeader>

    <div class="flex items-end gap-2">
      <div class="flex-1">
        <Input v-model="filter" placeholder="Filter by email…" />
      </div>
      <Select
        v-model="onboardedFilter"
        :options="[
          { value: 'all', label: 'All' },
          { value: 'yes', label: 'Onboarded' },
          { value: 'no', label: 'Not onboarded' },
        ]"
      />
    </div>

    <div v-if="isLoading" class="space-y-1">
      <div v-for="i in 6" :key="i" class="skeleton h-9" />
    </div>

    <div v-else-if="error" class="rounded-md border border-danger bg-danger-soft p-3 text-sm">
      <p class="font-medium text-danger">Failed to load subjects</p>
      <p class="mt-1 text-fg-muted">{{ errorMessage(error) }}</p>
    </div>

    <EmptyState
      v-else-if="!subjects?.length"
      :title="filter ? 'No matches' : 'No subjects yet'"
      hint="Invitations auto-create subjects. Manual creation is rare."
    />

    <div v-else class="overflow-hidden rounded-lg border border-border bg-surface">
      <table class="w-full text-sm">
        <thead class="border-b border-border bg-bg/30 text-left text-xs uppercase tracking-wider text-fg-subtle">
          <tr>
            <th class="px-3 py-2 font-medium">Email</th>
            <th class="px-3 py-2 font-medium">Display name</th>
            <th class="px-3 py-2 font-medium">Kind</th>
            <th class="px-3 py-2 font-medium">Status</th>
            <th class="px-3 py-2 font-medium">Onboarded</th>
            <th class="px-3 py-2 font-medium">ID</th>
            <th class="px-3 py-2 font-medium text-right">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="s in subjects" :key="s.id" class="border-b border-border last:border-0 hover:bg-surface-hover">
            <td class="px-3 py-2 text-fg">{{ s.email }}</td>
            <td class="px-3 py-2 text-fg-muted">{{ s.displayName ?? "—" }}</td>
            <td class="px-3 py-2"><Badge :tone="kindTone(s.kind)">{{ s.kind }}</Badge></td>
            <td class="px-3 py-2">
              <Badge :tone="s.active ? 'success' : 'neutral'">{{ s.active ? "active" : "inactive" }}</Badge>
            </td>
            <td class="px-3 py-2 text-xs text-fg-muted">{{ fmtDate(s.onboardedAt) }}</td>
            <td class="px-3 py-2"><MonoId :value="s.id" :truncate="20" /></td>
            <td class="px-3 py-2">
              <div class="flex justify-end gap-1">
                <Button size="sm" variant="ghost" @click="openRelink(s)">Relink…</Button>
                <Button v-if="s.active" size="sm" variant="danger" @click="onDeactivate(s)">Deactivate</Button>
                <Button v-else size="sm" variant="secondary" @click="onReactivate(s)">Reactivate</Button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <Dialog
      v-model:open="createOpen"
      title="New subject"
      description="Manually creates a subject + external identity link. Usually you want to invite instead."
    >
      <form class="flex flex-col gap-4" @submit.prevent="submit">
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Email
          <Input v-model="email" :error="errors.email" type="email" />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Display name (optional)
          <Input v-model="displayName" />
        </label>
        <div class="grid grid-cols-2 gap-3">
          <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
            Provider
            <Input v-model="provider" :error="errors.provider" />
          </label>
          <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
            Provider tenant
            <Input v-model="providerTenant" :error="errors.providerTenant" />
          </label>
        </div>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          External ID
          <Input v-model="externalId" :error="errors.externalId" placeholder="Keycloak sub claim" />
        </label>
        <div class="mt-2 flex justify-end gap-2">
          <Button variant="ghost" type="button" @click="createOpen = false">Cancel</Button>
          <Button variant="primary" type="submit" :loading="isSubmitting">Create</Button>
        </div>
      </form>
    </Dialog>

    <!-- Relink identity dialog -->
    <Dialog
      v-model:open="relinkOpen"
      :title="`Relink identity: ${relinkTarget?.email ?? ''}`"
      description="Use when the Keycloak user was deleted and re-created with a new sub claim. The subject keeps its history; only the external link is swapped."
    >
      <form class="flex flex-col gap-4" @submit.prevent="submitRelink">
        <div class="grid grid-cols-2 gap-3">
          <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
            Provider
            <Input v-model="relinkProvider" />
          </label>
          <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
            Provider tenant
            <Input v-model="relinkProviderTenant" />
          </label>
        </div>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Old external ID
          <Input v-model="relinkOldExternalId" placeholder="Previous Keycloak sub claim" />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          New external ID
          <Input v-model="relinkNewExternalId" placeholder="New Keycloak sub claim" />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Reason (optional)
          <Input v-model="relinkReason" />
        </label>
        <div class="mt-2 flex justify-end gap-2">
          <Button variant="ghost" type="button" @click="relinkOpen = false">Cancel</Button>
          <Button
            variant="primary"
            type="submit"
            :disabled="!relinkOldExternalId.trim() || !relinkNewExternalId.trim()"
          >Relink</Button>
        </div>
      </form>
    </Dialog>
  </div>
</template>
