# Obsidian documentation website

The documentation site uses React, TypeScript, and Vite. Its source is in
`Docs/website`. The welcome page is separate from the documentation reader.

## Pages and documentation sources

- `/` or `#/` opens the welcome page, with links to the docs and GitHub.
- `#/docs/getting-started/introduction` opens the documentation reader.
- Each guide has its own hash URL, so refreshes, direct links, and browser back/forward
  work on static hosting, including GitHub Pages, without a server rewrite.

Engine guides load directly from `Docs/markdown/**/*.md` through Vite's raw imports.
Editing a guide there updates the website on the next build (or through live reload
in development). Getting Started pages live in `src/markdown/getting-started`.
The older example pages in other `src/markdown` folders are not published.

`src/docs.ts` assigns each current engine Markdown file to its topic and URL in
`PAGE_METADATA`. When adding a guide, add its filename and topic to that map.
Unmapped files still appear automatically under Reference, so a guide is never
silently omitted. Only topics with published guides appear in the sidebar.

Search filters guides by topic, title, and full content. Links between Markdown
files navigate to their corresponding guide; code and diagram links open on GitHub.
The logo uses Vite's `BASE_URL` so it works under a repository base path.

## Build and preview locally

Install Node.js 22.12 or newer with npm. From the repository root, run:

```powershell
cd Docs/website
npm ci
npm run build
npm run preview
```

`npm ci` installs the dependencies from `package-lock.json`. The build runs the
TypeScript checks and creates the static site in `Docs/website/dist`. Open the
URL printed by the preview command (normally `http://localhost:4173/`).

For development with live reload, run `npm run dev` instead of building and
previewing.

## Deploy to GitHub Pages

This repository's project site URL is `https://pmdroide.github.io/obsidian/`.
Vite must build with the base path `/obsidian/` so JavaScript and CSS load from
that directory. The workflow below passes it through the build command; you
can keep the existing `vite.config.ts` for local development.

### 1. Check the Pages build locally

Run these commands from `Docs/website`:

```powershell
npm run build -- --base /obsidian/
npm run preview -- --base /obsidian/
```

Open `http://localhost:4173/obsidian/` (or the address printed by Vite).
Check the welcome page and follow its documentation link. Logo and application
assets already respect the repository base path.

### 2. Enable Pages

On GitHub, open the repository's **Settings > Pages**. Under **Build and
deployment**, select **GitHub Actions** as the source.

### 3. Add the deployment workflow

Create `.github/workflows/deploy-docs.yml` at the **repository root**, with:

```yaml
name: Deploy documentation to GitHub Pages

on:
  push:
    branches: [main]
    paths:
      - 'Docs/website/**'
      - 'Docs/markdown/**'
      - '.github/workflows/deploy-docs.yml'
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: pages
  cancel-in-progress: false

jobs:
  deploy:
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    defaults:
      run:
        working-directory: Docs/website
    steps:
      - name: Check out repository
        uses: actions/checkout@v7

      - name: Set up Node.js
        uses: actions/setup-node@v7
        with:
          node-version: '22'
          cache: npm
          cache-dependency-path: Docs/website/package-lock.json

      - name: Install dependencies
        run: npm ci

      - name: Build documentation
        run: npm run build -- --base /obsidian/

      - name: Configure Pages
        uses: actions/configure-pages@v6

      - name: Upload site
        uses: actions/upload-pages-artifact@v5
        with:
          path: Docs/website/dist

      - name: Deploy to GitHub Pages
        id: deployment
        uses: actions/deploy-pages@v5
```

The workflow follows [Vite's GitHub Pages deployment guide](https://vite.dev/guide/static-deploy.html#github-pages)
and [GitHub's custom Pages workflow requirements](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages),
with build and artifact paths adjusted for this repository.

### 4. Publish and update

Commit the website changes and workflow, then push or merge them into `main`.
The workflow deploys website changes on that branch. You can also run it from
**Actions > Deploy documentation to GitHub Pages > Run workflow** after the
workflow exists on the default branch.

When deployment succeeds, open the URL in the workflow's deployment output or
**Settings > Pages**. Future changes to `Docs/website` or `Docs/markdown` on `main` redeploy the site.

For a fork, replace `/obsidian/` in the build and preview commands with
`/<repository-name>/`, and use the fork owner's Pages URL. If publishing at
`https://<owner>.github.io/` or a custom domain's root, use `/` as the base path.
If deploying from a branch other than `main`, update `branches` and ensure the
`github-pages` environment allows deployment from that branch.
