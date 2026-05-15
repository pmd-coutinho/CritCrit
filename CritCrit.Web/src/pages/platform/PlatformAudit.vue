<script setup lang="ts">
import { computed, ref } from "vue";
import { usePlatformAudit } from "@/api/queries";
import { errorMessage } from "@/api/errors";
import PageHeader from "@/components/ui/PageHeader.vue";
import EmptyState from "@/components/ui/EmptyState.vue";
import Badge from "@/components/ui/Badge.vue";
import Button from "@/components/ui/Button.vue";
import Input from "@/components/ui/Input.vue";
import MonoId from "@/components/ui/MonoId.vue";

const actionInput = ref("");
const tenantInput = ref("");
const actorInput = ref("");

const filter = computed(() => ({
  action: actionInput.value.trim() || undefined,
  tenantId: tenantInput.value.trim() || undefined,
  actorExternalId: actorInput.value.trim() || undefined,
}));

const { data: events, isLoading, error, refetch } = usePlatformAudit(filter, 200);

const expanded = ref<Set<string>>(new Set());
function toggle(id: string) {
  if (expanded.value.has(id)) expanded.value.delete(id);
  else expanded.value.add(id);
}

function fmtRel(iso: string) {
  const dt = (Date.now() - new Date(iso).getTime()) / 1000;
  if (dt < 60) return `${Math.floor(dt)}s ago`;
  if (dt < 3600) return `${Math.floor(dt / 60)}m ago`;
  if (dt < 86400) return `${Math.floor(dt / 3600)}h ago`;
  return new Date(iso).toLocaleDateString();
}

function clearFilters() {
  actionInput.value = "";
  tenantInput.value = "";
  actorInput.value = "";
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <PageHeader title="Platform audit" subtitle="Every audit event across every brand. SuperAdmin only.">
      <template #actions>
        <Button variant="ghost" @click="clearFilters">Clear filters</Button>
        <Button variant="secondary" @click="refetch">Refresh</Button>
      </template>
    </PageHeader>

    <div class="grid grid-cols-1 gap-2 md:grid-cols-3">
      <Input v-model="actionInput" placeholder="Action (e.g. invitation.accepted)" />
      <Input v-model="tenantInput" placeholder="Tenant GUID" />
      <Input v-model="actorInput" placeholder="Actor external ID" />
    </div>

    <div v-if="isLoading" class="space-y-1">
      <div v-for="i in 8" :key="i" class="skeleton h-9" />
    </div>

    <div v-else-if="error" class="rounded-md border border-danger bg-danger-soft p-3 text-sm">
      <p class="font-medium text-danger">Failed to load audit log</p>
      <p class="mt-1 text-fg-muted">{{ errorMessage(error) }}</p>
    </div>

    <EmptyState v-else-if="!events?.length" title="No audit events match" />

    <div v-else class="overflow-hidden rounded-lg border border-border bg-surface">
      <table class="w-full text-sm">
        <thead class="border-b border-border bg-bg/30 text-left text-xs uppercase tracking-wider text-fg-subtle">
          <tr>
            <th class="w-32 px-3 py-2 font-medium">When</th>
            <th class="px-3 py-2 font-medium">Action</th>
            <th class="px-3 py-2 font-medium">Actor</th>
            <th class="px-3 py-2 font-medium">Tenant</th>
            <th class="px-3 py-2 font-medium">Target</th>
            <th class="px-3 py-2 font-medium">Reason</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="ev in events" :key="ev.id">
            <tr
              class="cursor-pointer border-b border-border last:border-0 hover:bg-surface-hover"
              @click="toggle(String(ev.id))"
            >
              <td class="px-3 py-2 text-xs text-fg-muted" :title="ev.occurredAt">{{ fmtRel(ev.occurredAt) }}</td>
              <td class="px-3 py-2"><Badge>{{ ev.action }}</Badge></td>
              <td class="px-3 py-2 text-xs text-fg-muted">{{ ev.actorExternalId }}</td>
              <td class="px-3 py-2">
                <MonoId v-if="ev.tenantId" :value="String(ev.tenantId)" :truncate="16" />
                <span v-else class="text-fg-subtle">—</span>
              </td>
              <td class="px-3 py-2">
                <MonoId v-if="ev.targetOrgNodeId" :value="String(ev.targetOrgNodeId)" :truncate="16" />
                <span v-else class="text-fg-subtle">—</span>
              </td>
              <td class="px-3 py-2 text-xs text-fg-muted">{{ ev.reason ?? "—" }}</td>
            </tr>
            <tr v-if="expanded.has(String(ev.id))" class="border-b border-border bg-bg/40 last:border-0">
              <td colspan="6" class="px-3 py-3">
                <pre class="overflow-x-auto rounded-sm bg-bg p-3 font-mono text-xs text-fg-muted">{{ JSON.stringify(ev.details ?? {}, null, 2) }}</pre>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>
  </div>
</template>
