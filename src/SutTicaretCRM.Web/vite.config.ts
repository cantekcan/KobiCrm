import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Geliştirmede /api istekleri .NET API'ye yönlendirilir (varsayılan http://localhost:5080).
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { "/api": process.env.API_URL ?? "http://localhost:5080" } },
  build: { outDir: "dist", sourcemap: false },
  test: { environment: "jsdom", globals: true },
});
