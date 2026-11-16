import js from '@eslint/js';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    ignores: ['**/dist/**', '**/out/**', '**/release/**', '**/.types/**', '**/node_modules/**'],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    rules: {
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
      '@typescript-eslint/consistent-type-imports': 'error',
    },
  },
  {
    // Typed rules: slower, so source files only.
    files: ['packages/*/src/**/*.{ts,tsx,cts}', 'apps/*/src/**/*.{ts,tsx,cts}'],
    languageOptions: { parserOptions: { projectService: true, tsconfigRootDir: import.meta.dirname } },
    rules: {
      '@typescript-eslint/no-floating-promises': ['error', { ignoreVoid: false }],
      '@typescript-eslint/no-misused-promises': 'error',
      '@typescript-eslint/switch-exhaustiveness-check': [
        'error',
        { considerDefaultExhaustiveForUnions: false },
      ],
    },
  },
  {
    files: ['**/scripts/**/*.mjs'],
    languageOptions: { globals: { console: 'readonly', process: 'readonly' } },
  },
  {
    // The engine packages must stay portable: browser, worker, Node, Electron renderer.
    files: ['packages/text/**/*.ts', 'packages/markdown/**/*.ts'],
    // The random-operation runner reads its seeds from the environment; it runs only under Vitest in Node.
    ignores: ['packages/*/test/support/**'],
    rules: {
      'no-restricted-globals': ['error', 'window', 'document', 'navigator', 'localStorage', 'process'],
      'no-restricted-imports': ['error', { patterns: ['node:*', 'preact', 'preact/*', '@preact/*'] }],
    },
  },
);
