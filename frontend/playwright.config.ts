import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  timeout: 90_000,
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: "list",
  use: {
    baseURL: "http://127.0.0.1:3017",
    colorScheme: "dark",
    trace: "retain-on-failure"
  },
  webServer: {
    command: "npm run dev -- --port 3017",
    url: "http://127.0.0.1:3017",
    reuseExistingServer: !process.env.CI,
    timeout: 120_000
  }
});
