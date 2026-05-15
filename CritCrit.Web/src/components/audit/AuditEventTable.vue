<script setup lang="ts">
import { ref } from "vue";
import type { AuditEventResponse } from "@/api/generated";
import Badge from "@/components/ui/Badge.vue";
import MonoId from "@/components/ui/MonoId.vue";

defineProps<{ events: AuditEventResponse[]; platform?: boolean }>();

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

function severityTone(severity: string) {
  if (severity === "critical") return "danger";
  if (severity === "warn") return "warn";
  return "neutral";
}

function actionLabel(action: string) {
  return action
    .replaceAll(".", " ")
    .replaceAll("-", " ");
}

function summary(ev: AuditEventResponse) {
  const target = ev.targetLabel || ev.targetPublicId || ev.targetOrgNodeId;
  const subject = ev.subjectPublicId || ev.subjectId;
  if (target && subject) return `${target} · ${subject}`;
  return target || subject || ev.category;
}
</script>

<template>
  <div class="overflow-hidden rounded-lg border border-border bg-surface">
    <table class="w-full text-sm">
      <thead class="sticky top-0 border-b border-border bg-bg/70 text-left text-xs uppercase tracking-wider text-fg-subtle">
        <tr>
          <th class="w-28 px-3 py-2 font-medium">When</th>
          <th class="px-3 py-2 font-medium">Action</th>
          <th class="px-3 py-2 font-medium">Severity</th>
          <th class="px-3 py-2 font-medium">Actor</th>
          <th v-if="platform" class="px-3 py-2 font-medium">Tenant</th>
          <th class="px-3 py-2 font-medium">Target</th>
          <th class="px-3 py-2 font-medium">Support</th>
        </tr>
      </thead>
      <tbody>
        <template v-for="ev in events" :key="ev.id">
          <tr class="cursor-pointer border-b border-border last:border-0 hover:bg-surface-hover" @click="toggle(ev.id)">
            <td class="px-3 py-2 text-xs text-fg-muted" :title="ev.occurredAt">{{ fmtRel(ev.occurredAt) }}</td>
            <td class="px-3 py-2">
              <div class="flex min-w-0 flex-col gap-1">
                <span class="font-medium text-fg">{{ actionLabel(ev.action) }}</span>
                <span class="font-mono text-xs text-fg-subtle">{{ ev.action }}</span>
              </div>
            </td>
            <td class="px-3 py-2">
              <div class="flex items-center gap-1.5">
                <Badge :tone="severityTone(ev.severity)">{{ ev.severity }}</Badge>
                <Badge>{{ ev.category }}</Badge>
              </div>
            </td>
            <td class="px-3 py-2">
              <div class="flex flex-col gap-0.5">
                <MonoId :value="ev.actorSubjectPublicId || ev.actorExternalId" :truncate="18" />
                <span class="text-xs text-fg-subtle">{{ ev.actorKind }}</span>
              </div>
            </td>
            <td v-if="platform" class="px-3 py-2">
              <MonoId v-if="ev.tenantPublicId || ev.tenantId" :value="ev.tenantPublicId || ev.tenantId || ''" :truncate="18" />
              <span v-else class="text-fg-subtle">-</span>
            </td>
            <td class="px-3 py-2">
              <div class="flex min-w-0 flex-col gap-0.5">
                <span class="truncate text-fg">{{ summary(ev) }}</span>
                <MonoId v-if="ev.targetPublicId || ev.targetOrgNodeId" :value="ev.targetPublicId || ev.targetOrgNodeId || ''" :truncate="20" />
              </div>
            </td>
            <td class="px-3 py-2">
              <MonoId v-if="ev.supportId" :value="ev.supportId" :truncate="12" />
              <span v-else class="text-fg-subtle">-</span>
            </td>
          </tr>
          <tr v-if="expanded.has(ev.id)" class="border-b border-border bg-bg/40 last:border-0">
            <td :colspan="platform ? 7 : 6" class="px-3 py-3">
              <div class="grid gap-3 md:grid-cols-[minmax(0,1fr)_minmax(0,1.5fr)]">
                <div class="space-y-2 text-xs">
                  <div v-if="ev.reason" class="rounded-md border border-border bg-surface p-2">
                    <div class="mb-1 uppercase tracking-wider text-fg-subtle">Reason</div>
                    <div class="text-fg-muted">{{ ev.reason }}</div>
                  </div>
                  <div v-if="ev.changes?.length" class="rounded-md border border-border bg-surface p-2">
                    <div class="mb-1 uppercase tracking-wider text-fg-subtle">Changes</div>
                    <div v-for="change in ev.changes" :key="change.field" class="font-mono text-fg-muted">
                      {{ change.field }}: {{ String(change.oldValue ?? "-") }} -> {{ String(change.newValue ?? "-") }}
                    </div>
                  </div>
                  <div v-if="ev.request" class="rounded-md border border-border bg-surface p-2">
                    <div class="mb-1 uppercase tracking-wider text-fg-subtle">Request</div>
                    <div class="font-mono text-fg-muted">{{ ev.request.method }} {{ ev.request.path }}</div>
                  </div>
                </div>
                <pre class="overflow-x-auto rounded-sm bg-bg p-3 font-mono text-xs text-fg-muted">{{ JSON.stringify(ev.details ?? {}, null, 2) }}</pre>
              </div>
            </td>
          </tr>
        </template>
      </tbody>
    </table>
  </div>
</template>
