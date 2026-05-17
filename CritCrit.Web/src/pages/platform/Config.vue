<script setup lang="ts">
import { computed, ref } from "vue";
import { toast } from "vue-sonner";
import {
  useConfigSchemas,
  useConfigSchemaDrafts,
  useConfigSchemaVersions,
  useCreateConfigSchema,
  usePublishConfigDraft,
} from "@/api/queries";
import type {
  ConfigSchemaDefinition,
  ConfigSchemaResponse,
  CreateConfigSchemaRequest,
} from "@/api/generated";
import { errorMessage } from "@/api/errors";
import PageHeader from "@/components/ui/PageHeader.vue";
import EmptyState from "@/components/ui/EmptyState.vue";
import Badge from "@/components/ui/Badge.vue";
import Button from "@/components/ui/Button.vue";
import Dialog from "@/components/ui/Dialog.vue";
import Input from "@/components/ui/Input.vue";

const includeArchived = ref(false);
const { data: schemas, isLoading, error } = useConfigSchemas(includeArchived);

const selected = ref<string>("");
const { data: drafts } = useConfigSchemaDrafts(selected);
const { data: versions } = useConfigSchemaVersions(selected);

const createSchema = useCreateConfigSchema();
const publishDraft = usePublishConfigDraft(selected);

// ─── Create schema dialog ───
const createOpen = ref(false);
const newCode = ref("");
const newName = ref("");
const newDescription = ref("");
const newDraftName = ref("v1");
const newDefinitionJson = ref(JSON.stringify(
  {
    name: "POS Bridge",
    description: null,
    keys: [
      {
        code: "usetaxcalc",
        name: "Use tax calc",
        valueType: "Boolean",
        defaultValue: { jsonValue: "true" },
      },
    ],
  },
  null,
  2,
));
const submitting = ref(false);

async function submitCreate() {
  submitting.value = true;
  try {
    let definition: ConfigSchemaDefinition;
    try {
      definition = JSON.parse(newDefinitionJson.value);
    } catch (err) {
      toast.error("Invalid JSON in definition", { description: errorMessage(err) });
      submitting.value = false;
      return;
    }
    const body: CreateConfigSchemaRequest = {
      code: newCode.value.trim(),
      name: newName.value.trim(),
      description: newDescription.value.trim() || null,
      draftName: newDraftName.value.trim() || "v1",
      definition,
    };
    await createSchema.mutateAsync(body);
    toast.success("Schema created", { description: body.code });
    createOpen.value = false;
  } catch (err) {
    toast.error("Create failed", { description: errorMessage(err) });
  } finally {
    submitting.value = false;
  }
}

async function onPublish(draftId: string, expectedVersion: number) {
  try {
    await publishDraft.mutateAsync({ draftId, body: { expectedVersion, reason: null } });
    toast.success("Version published");
  } catch (err) {
    toast.error("Publish failed", { description: errorMessage(err) });
  }
}

function selectSchema(s: ConfigSchemaResponse) {
  selected.value = s.code;
}

function fmtDate(iso: string) {
  return new Date(iso).toLocaleDateString();
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <PageHeader title="Config schemas" subtitle="Global schemas. Drafts → versions → assignments on brand nodes.">
      <template #actions>
        <label class="flex items-center gap-2 text-xs text-fg-muted">
          <input type="checkbox" v-model="includeArchived" class="h-4 w-4 rounded-sm border-border bg-surface text-accent" />
          Show archived
        </label>
        <Button variant="primary" @click="createOpen = true">+ Schema</Button>
      </template>
    </PageHeader>

    <div v-if="isLoading" class="space-y-1">
      <div v-for="i in 4" :key="i" class="skeleton h-9" />
    </div>

    <div v-else-if="error" class="rounded-md border border-danger bg-danger-soft p-3 text-sm">
      <p class="font-medium text-danger">Failed to load schemas</p>
      <p class="mt-1 text-fg-muted">{{ errorMessage(error) }}</p>
    </div>

    <EmptyState v-else-if="!schemas?.length" title="No schemas yet" hint="Define a global config schema to get started.">
      <Button variant="primary" @click="createOpen = true">+ Schema</Button>
    </EmptyState>

    <div v-else class="grid grid-cols-1 gap-6 lg:grid-cols-[1fr_24rem]">
      <div class="overflow-hidden rounded-lg border border-border bg-surface">
        <table class="w-full text-sm">
          <thead class="border-b border-border bg-bg/30 text-left text-xs uppercase tracking-wider text-fg-subtle">
            <tr>
              <th class="px-3 py-2 font-medium">Code</th>
              <th class="px-3 py-2 font-medium">Name</th>
              <th class="px-3 py-2 font-medium">Latest</th>
              <th class="px-3 py-2 font-medium">Status</th>
              <th class="px-3 py-2 font-medium">Updated</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="s in schemas"
              :key="s.code"
              class="cursor-pointer border-b border-border last:border-0 hover:bg-surface-hover"
              :class="{ 'bg-surface-hover': selected === s.code }"
              @click="selectSchema(s)"
            >
              <td class="px-3 py-2 font-mono text-xs text-fg">{{ s.code }}</td>
              <td class="px-3 py-2">{{ s.name }}</td>
              <td class="px-3 py-2 text-fg-muted">{{ s.latestPublishedVersion ?? "—" }}</td>
              <td class="px-3 py-2">
                <Badge v-if="s.archived" tone="warn">archived</Badge>
                <Badge v-else tone="success">active</Badge>
              </td>
              <td class="px-3 py-2 text-xs text-fg-muted">{{ fmtDate(s.updatedAt) }}</td>
            </tr>
          </tbody>
        </table>
      </div>

      <aside v-if="selected" class="flex flex-col gap-4 rounded-lg border border-border bg-surface p-4">
        <h2 class="font-mono text-xs uppercase tracking-wider text-fg-subtle">{{ selected }}</h2>

        <section>
          <p class="text-xs uppercase tracking-wider text-fg-muted">Drafts</p>
          <ul v-if="drafts?.length" class="mt-2 space-y-2">
            <li v-for="d in drafts" :key="d.id" class="rounded-md border border-border p-2 text-xs">
              <div class="flex items-center justify-between gap-2">
                <span class="text-fg">{{ d.name }}</span>
                <Badge v-if="d.published" tone="success">v{{ d.publishedAsVersion }}</Badge>
                <Badge v-else-if="d.archived" tone="warn">archived</Badge>
                <Badge v-else tone="accent">draft</Badge>
              </div>
              <div class="mt-1 flex items-center justify-between text-fg-subtle">
                <span>base v{{ d.baseVersion ?? "—" }}</span>
                <Button v-if="!d.published && !d.archived" size="sm" variant="secondary" @click="onPublish(d.id, d.version)">Publish</Button>
              </div>
            </li>
          </ul>
          <p v-else class="mt-2 text-xs text-fg-subtle">No drafts.</p>
        </section>

        <section>
          <p class="text-xs uppercase tracking-wider text-fg-muted">Published versions</p>
          <ul v-if="versions?.length" class="mt-2 space-y-1">
            <li v-for="v in versions" :key="v.version" class="flex items-center justify-between text-xs">
              <span class="font-mono text-fg">v{{ v.version }}</span>
              <span class="text-fg-subtle">{{ fmtDate(v.publishedAt) }}</span>
            </li>
          </ul>
          <p v-else class="mt-2 text-xs text-fg-subtle">No published versions yet.</p>
        </section>
      </aside>
    </div>

    <!-- Create schema dialog -->
    <Dialog v-model:open="createOpen" title="New config schema" description="Definition is JSON. Validated by the server.">
      <form class="flex flex-col gap-4" @submit.prevent="submitCreate">
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Code
          <Input v-model="newCode" placeholder="pos-bridge" />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Name
          <Input v-model="newName" placeholder="POS Bridge" />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Description
          <Input v-model="newDescription" />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Initial draft name
          <Input v-model="newDraftName" />
        </label>
        <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
          Definition JSON
          <textarea
            v-model="newDefinitionJson"
            rows="14"
            class="rounded-md border border-border bg-surface p-2 font-mono text-xs text-fg focus:outline-none focus:border-accent"
          />
        </label>
        <div class="mt-2 flex justify-end gap-2">
          <Button variant="ghost" type="button" @click="createOpen = false">Cancel</Button>
          <Button variant="primary" type="submit" :loading="submitting">Create</Button>
        </div>
      </form>
    </Dialog>
  </div>
</template>
