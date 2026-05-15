import "./design/fonts.css";
import "./design/tokens.css";

import { createApp } from "vue";
import { createPinia } from "pinia";
import { VueQueryPlugin, QueryClient } from "@tanstack/vue-query";

import App from "./App.vue";
import { router } from "./router/index";
import { useAuthStore } from "./stores/auth";
import { applyStoredTheme } from "./stores/theme";

async function bootstrap() {
  applyStoredTheme();

  const app = createApp(App);
  const pinia = createPinia();
  app.use(pinia);

  const auth = useAuthStore();
  await auth.initialize();

  app.use(router);
  app.use(VueQueryPlugin, {
    queryClient: new QueryClient({
      defaultOptions: {
        queries: {
          staleTime: 30_000,
          retry: 1,
          refetchOnWindowFocus: false,
        },
      },
    }),
  });

  app.mount("#app");
}

bootstrap();
