import js from '@eslint/js';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    // The C# tree stays until the final phase of the TypeScript port; nothing in it is linted.
    ignores: [
      '**/dist/**',
      '**/out/**',
      '**/release/**',
      '**/.types/**',
      '**/node_modules/**',
      'app/**',
      'features/**',
      'shared/**',
    ],
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
    files: ['**/scripts/**/*.mjs'],
    languageOptions: { globals: { console: 'readonly', process: 'readonly' } },
  },
  {
    // The engine packages must stay portable: browser, worker, Node, Electron renderer.
    files: ['packages/text/**/*.ts', 'packages/markdown/**/*.ts'],
    rules: {
      'no-restricted-globals': ['error', 'window', 'document', 'navigator', 'localStorage', 'process'],
      'no-restricted-imports': ['error', { patterns: ['node:*', 'preact', 'preact/*', '@preact/*'] }],
    },
  },
);
