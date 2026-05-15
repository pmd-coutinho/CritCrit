<script setup lang="ts">
import { computed, ref } from "vue";
import { useRoute } from "vue-router";
import { useBrandAudit, type AuditFilter } from "@/api/queries";
import { errorMessage, supportId } from "@/api/errors";
import PageHeader from "@/components/ui/PageHeader.vue";
import EmptyState from "@/components/ui/EmptyState.vue";
import Button from "@/components/ui/Button.vue";
import Input from "@/components/ui/Input.vue";
import MonoId from "@/components/ui/MonoId.vue";
import AuditEventTable from "@/components/audit/AuditEventTable.vue";

const route = useRoute();
const brandId = computed(() => route.params.brandId as string);

const action = ref("");
const category = ref("");
const severity = ref("");
const target = ref("");
const subject = ref("");
const actor = ref("");
const support = ref("");

const filter = computed<AuditFilter>(() => ({
  action: action.value,
  category: category.value,
  severity: severity.value,
  targetOrgNodeId: target.value,
  subjectId: subject.value,
  actorExternalId: actor.value,
  supportId: support.value,
}));

const { data: events, isLoading, error, refetch } = useBrandAudit(brandId, filter, 200);

function clearFilters() {
  action.value = "";
  category.value = "";
  severity.value = "";
  target.value = "";
  subject.value = "";
  actor.value = "";
  support.value = "";
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <PageHeader title="Audit log" subtitle="Brand audit history for owner support.">
      <template #actions>
        <Button variant="ghost" @click="clearFilters">Clear filters</Button>
        <Button variant="secondary" @click="refetch">Refresh</Button>
      </template>
    </PageHeader>

    <div class="grid grid-cols-1 gap-2 md:grid-cols-4">
      <Input v-model="action" placeholder="Action" />
      <Input v-model="category" placeholder="Category" />
      <Input v-model="severity" placeholder="Severity" />
      <Input v-model="support" placeholder="Support ID" />
      <Input v-model="target" placeholder="Target ID" />
      <Input v-model="subject" placeholder="Subject ID" />
      <Input v-model="actor" placeholder="Actor ID" />
    </div>

    <div v-if="isLoading" class="space-y-1">
      <div v-for="i in 8" :key="i" class="skeleton h-9" />
    </div>

    <div v-else-if="error" class="rounded-md border border-danger bg-danger-soft p-3 text-sm">
      <p class="font-medium text-danger">Failed to load audit</p>
      <p class="mt-1 text-fg-muted">{{ errorMessage(error) }}</p>
      <p v-if="supportId(error)" class="mt-2 text-xs text-fg-muted">Support <MonoId :value="supportId(error)!" :truncate="16" /></p>
    </div>

    <EmptyState v-else-if="!events?.length" title="No audit events match" />
    <AuditEventTable v-else :events="events" />
  </div>
</template>
