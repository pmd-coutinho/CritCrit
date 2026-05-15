<script setup lang="ts">
import { computed, ref } from "vue";
import { useRouter, RouterLink } from "vue-router";
import { toast } from "vue-sonner";
import { useAuthStore } from "@/stores/auth";
import { useThemeStore } from "@/stores/theme";
import { useMyBrands, useCreateBrand } from "@/api/queries";
import { errorMessage } from "@/api/errors";
import Button from "@/components/ui/Button.vue";
import Badge from "@/components/ui/Badge.vue";
import Input from "@/components/ui/Input.vue";
import Dialog from "@/components/ui/Dialog.vue";
import EmptyState from "@/components/ui/EmptyState.vue";

const router = useRouter();
const auth = useAuthStore();
const theme = useThemeStore();

const { data: brands, isLoading, error } = useMyBrands();
const createBrand = useCreateBrand();

const createOpen = ref(false);
const newCode = ref("");
const newName = ref("");
const submitting = ref(false);

function openCreate() {
  newCode.value = "";
  newName.value = "";
  createOpen.value = true;
}

async function submitCreate() {
  const code = newCode.value.trim();
  const name = newName.value.trim();
  if (!code || !name) {
    toast.error("Code and name are required");
    return;
  }
  submitting.value = true;
  try {
    const created = await createBrand.mutateAsync({ code, name });
    toast.success("Brand created", { description: created.name });
    createOpen.value = false;
    router.push(`/brands/${created.id}/tree`);
  } catch (err) {
    toast.error("Create failed", { description: errorMessage(err) });
  } finally {
    submitting.value = false;
  }
}

const filter = ref("");
const filtered = computed(() => {
  const q = filter.value.trim().toLowerCase();
  if (!q) return brands.value ?? [];
  return (brands.value ?? []).filter(
    (b) => b.name.toLowerCase().includes(q) || b.code.toLowerCase().includes(q) || b.id.toLowerCase().includes(q),
  );
});

const manual = ref("");
function openManual() {
  const v = manual.value.trim();
  if (!v) return;
  router.push(`/brands/${v}/tree`);
}

function open(id: string) {
  router.push(`/brands/${id}/tree`);
}

const displayName = computed(() => auth.user?.name ?? auth.user?.username ?? "");


const roleTone = (r: string | null) =>
  r === "Owner" ? "danger" : r === "Admin" ? "warn" : r === "Member" ? "accent" : "neutral";
</script>

<template>
  <div class="min-h-screen">
    <header class="flex h-12 items-center justify-between border-b border-border px-6">
      <div class="flex items-center gap-2 font-mono text-xs uppercase tracking-[0.18em] text-fg-muted">
        <span class="inline-block h-1.5 w-1.5 rounded-full bg-accent" />
        critcrit
      </div>
      <div class="flex items-center gap-3 text-xs text-fg-muted">
        <RouterLink
          v-if="auth.isSuperAdmin"
          to="/platform/subjects"
          class="inline-flex items-center rounded-xs border border-accent/40 px-1.5 py-0.5 font-mono text-[0.625rem] uppercase tracking-widest text-accent hover:bg-accent-soft"
        >platform</RouterLink>
        <span>{{ displayName }}</span>
        <button class="hover:text-fg" @click="theme.toggle()">{{ theme.mode === "dark" ? "☾" : "☀" }}</button>
        <button class="hover:text-fg" @click="auth.logout()">sign out</button>
      </div>
    </header>

    <main class="mx-auto flex max-w-3xl flex-col gap-10 px-6 py-16">
      <section>
        <div class="flex items-end justify-between gap-4">
          <div>
            <p class="font-mono text-xs uppercase tracking-widest text-fg-subtle">Workspaces</p>
            <h1 class="mt-2 text-2xl font-semibold tracking-tight text-fg">Where to?</h1>
            <p class="mt-2 text-sm text-fg-muted">
              {{ auth.isSuperAdmin ? "Every brand on the platform." : "Brands you have access to." }}
            </p>
          </div>
          <Button v-if="auth.isSuperAdmin" variant="primary" @click="openCreate">+ New brand</Button>
        </div>

        <div class="mt-6 flex items-center gap-2">
          <div class="flex-1">
            <Input v-model="filter" placeholder="Filter by name, code, id…" />
          </div>
        </div>

        <div v-if="isLoading" class="mt-4 space-y-1">
          <div v-for="i in 4" :key="i" class="skeleton h-12" />
        </div>

        <div v-else-if="error" class="mt-4 rounded-md border border-danger bg-danger-soft p-3 text-sm">
          <p class="font-medium text-danger">Failed to load brands</p>
          <p class="mt-1 text-fg-muted">{{ errorMessage(error) }}</p>
        </div>

        <EmptyState
          v-else-if="!filtered.length"
          :title="brands?.length ? 'No matches' : 'No brands yet'"
          :hint="brands?.length ? 'Tweak the filter, or paste a brand ID below.' : auth.isSuperAdmin ? 'Create the first brand via the platform API.' : 'Ask an admin to invite you to one.'"
        />

        <ul v-else class="mt-4 divide-y divide-border overflow-hidden rounded-lg border border-border bg-surface">
          <li v-for="b in filtered" :key="b.id">
            <button
              class="flex w-full items-center gap-4 px-4 py-3 text-left transition-colors hover:bg-surface-hover"
              @click="open(b.id)"
            >
              <div class="min-w-0 flex-1">
                <div class="flex items-center gap-2">
                  <span class="truncate text-sm font-medium text-fg">{{ b.name }}</span>
                  <span class="font-mono text-xs text-fg-subtle">{{ b.code }}</span>
                  <Badge v-if="b.archived" tone="warn">archived</Badge>
                </div>
                <div class="mt-0.5 font-mono text-xs text-fg-subtle">{{ b.id }}</div>
              </div>
              <Badge v-if="b.highestRole" :tone="roleTone(b.highestRole)">{{ b.highestRole }}</Badge>
              <Badge v-else tone="accent">superadmin</Badge>
              <span class="text-fg-subtle">→</span>
            </button>
          </li>
        </ul>
      </section>

      <section>
        <p class="font-mono text-xs uppercase tracking-widest text-fg-subtle">Direct entry</p>
        <p class="mt-2 text-sm text-fg-muted">Paste a brand ID you already know.</p>
        <form class="mt-3 flex items-end gap-2" @submit.prevent="openManual">
          <div class="flex-1">
            <Input v-model="manual" placeholder="br_..." />
          </div>
          <Button type="submit" variant="secondary">Open</Button>
        </form>
      </section>

      <Dialog
        v-model:open="createOpen"
        title="New brand"
        description="Becomes its own tenant. Code is permanent."
      >
        <form class="flex flex-col gap-4" @submit.prevent="submitCreate">
          <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
            Code
            <Input v-model="newCode" placeholder="ACME" />
          </label>
          <label class="flex flex-col gap-1 text-xs uppercase tracking-wider text-fg-muted">
            Name
            <Input v-model="newName" placeholder="Acme Corp" />
          </label>
          <div class="mt-2 flex justify-end gap-2">
            <Button variant="ghost" type="button" @click="createOpen = false">Cancel</Button>
            <Button variant="primary" type="submit" :loading="submitting">Create brand</Button>
          </div>
        </form>
      </Dialog>
    </main>
  </div>
</template>
