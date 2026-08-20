import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

/**
 * Unit tests for the panel's pure modules — tree maths, JWT claim reading, translation
 * lookup. Deliberately not a component test setup: what these cover is logic that is silent
 * when it goes wrong, whereas a broken button is obvious the moment you look at the page.
 */
export default defineConfig({
  test: {
    // node, not jsdom: nothing under test touches the DOM.
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
});
