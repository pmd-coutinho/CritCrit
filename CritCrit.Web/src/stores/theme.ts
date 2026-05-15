import { defineStore } from "pinia";
import { ref, watch } from "vue";

type Mode = "dark" | "light";

const STORAGE_KEY = "critcrit.theme";

function resolveInitial(): Mode {
  const stored = localStorage.getItem(STORAGE_KEY);
  if (stored === "dark" || stored === "light") return stored;
  return window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark";
}

function apply(mode: Mode) {
  document.documentElement.classList.toggle("dark", mode === "dark");
  document.documentElement.classList.toggle("light", mode === "light");
}

export function applyStoredTheme() {
  apply(resolveInitial());
}

export const useThemeStore = defineStore("theme", () => {
  const mode = ref<Mode>(resolveInitial());

  watch(mode, (next) => {
    localStorage.setItem(STORAGE_KEY, next);
    apply(next);
  });

  function toggle() {
    mode.value = mode.value === "dark" ? "light" : "dark";
  }

  return { mode, toggle };
});
