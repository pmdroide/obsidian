/// <reference types="vite/client" />

declare module '*.md?raw' {
  const content: string;
  export default content;
}

// Vite's `import.meta.glob`. Declared explicitly because this project has no
// tsconfig.json, so the `vite/client` ImportMeta augmentation isn't applied.
interface ImportMeta {
  glob: <T = unknown>(
    pattern: string | string[],
    options?: { query?: string; import?: string; eager?: boolean }
  ) => Record<string, T>;
}
