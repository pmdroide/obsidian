import {
  Boxes, Code2, Image, Joystick, Monitor, PackageOpen, Pencil,
  PersonStanding, Rocket, Volume2, BookOpen,
} from 'lucide-react';

export const GITHUB_URL = 'https://github.com/pmdroide/obsidian';

export const TOPICS = [
  { id: 'getting-started', label: 'Getting Started', icon: Rocket },
  { id: 'scene', label: 'Scene', icon: Boxes },
  { id: 'code', label: 'Code', icon: Code2 },
  { id: 'editor', label: 'Editor', icon: Pencil },
  { id: 'assets', label: 'Assets', icon: PackageOpen },
  { id: 'rendering', label: 'Rendering', icon: Image },
  { id: 'gameplay', label: 'Gameplay & Input', icon: Joystick },
  { id: 'animation', label: 'Animation', icon: PersonStanding },
  { id: 'ui', label: 'UI', icon: Monitor },
  { id: 'sound', label: 'Sound', icon: Volume2 },
  { id: 'exporting-standalone', label: 'Building & Exporting', icon: PackageOpen },
  { id: 'reference', label: 'Reference', icon: BookOpen },
];

// The engine's Markdown folder is the source of truth: no copies to keep in sync.
const ENGINE_MARKDOWN = import.meta.glob('../../markdown/**/*.md', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>;

const STARTER_MARKDOWN = import.meta.glob('./markdown/getting-started/*.md', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>;

type PageMetadata = { topic: string; slug: string; title: string };

const PAGE_METADATA: Record<string, PageMetadata> = {
  'Scenes_and_Game_Flow.md': { topic: 'scene', slug: 'game-flow', title: 'Scenes & Game Flow' },
  'Gameobject_Components.md': { topic: 'scene', slug: 'gameobject-components', title: 'GameObject Components' },
  'Persistent_GameObjects.md': { topic: 'scene', slug: 'persistent-gameobjects', title: 'Persistent GameObjects' },
  'Script_Behaviours.md': { topic: 'code', slug: 'script-behaviours', title: 'Script Behaviours' },
  'Editor_Architecture.md': { topic: 'editor', slug: 'architecture', title: 'Anvil Editor Architecture' },
  'Importing Structure.md': { topic: 'assets', slug: 'importing', title: 'Importing Assets' },
  'Material_Component.md': { topic: 'rendering', slug: 'materials', title: 'Materials & Water' },
  'Scene_Lighting_and_Fog.md': { topic: 'rendering', slug: 'lighting-and-fog', title: 'Scene Lighting & Fog' },
  'Input Architecture.md': { topic: 'gameplay', slug: 'input', title: 'Input Architecture' },
  'Steam_Multiplayer.md': { topic: 'gameplay', slug: 'steam-multiplayer', title: 'Steam Multiplayer' },
  'Collisions_and_Interaction.md': { topic: 'gameplay', slug: 'collisions-and-interaction', title: 'Collisions & Interaction' },
  'Skeletal_Animation.md': { topic: 'animation', slug: 'skeletal-animation', title: 'Skeletal Animation' },
  'Ragdoll_Physics.md': { topic: 'animation', slug: 'ragdolls', title: 'Ragdoll Physics' },
  'VistaUI_Architecture.md': { topic: 'ui', slug: 'vista', title: 'Vista UI' },
  'Audio_Architecture.md': { topic: 'sound', slug: 'architecture', title: 'Audio Architecture' },
  'setup_fmod.md': { topic: 'sound', slug: 'setup-fmod', title: 'Setting Up FMOD' },
  'Exporting Shaders to Unity URP.md': { topic: 'exporting-standalone', slug: 'unity-urp', title: 'Exporting Shaders to Unity URP' },
};

export type DocPage = {
  slug: string;
  title: string;
  topic: string;
  content: string;
  source: string;
  filename: string;
};

export function headingId(text: string): string {
  return text.toLowerCase().replace(/[^\p{L}\p{N}_\s-]/gu, '').trim().replace(/\s/g, '-');
}

const enginePages: DocPage[] = Object.entries(ENGINE_MARKDOWN).map(([path, content]) => {
  const filename = path.replace('../../markdown/', '');
  const metadata = PAGE_METADATA[filename];
  const title = metadata?.title ?? content.match(/^#\s+(.+)$/m)?.[1] ?? filename.replace(/\.md$/, '').replace(/_/g, ' ');
  const topic = metadata?.topic ?? 'reference';
  return {
    slug: `${topic}/${metadata?.slug ?? headingId(filename.replace(/\.md$/, ''))}`,
    topic, title, content, filename, source: `Docs/markdown/${filename}`,
  };
});

const starterPages: DocPage[] = Object.entries(STARTER_MARKDOWN).map(([path, content]) => {
  const filename = path.replace('./markdown/', '');
  return {
    slug: filename.replace(/\.md$/, ''), topic: 'getting-started',
    title: content.match(/^#\s+(.+)$/m)?.[1] ?? filename,
    content, filename, source: `Docs/website/src/markdown/${filename}`,
  };
});

const STARTER_ORDER = ['getting-started/introduction', 'getting-started/first-steps', 'getting-started/reporting-issues'];

export const DOC_PAGES = [...starterPages, ...enginePages].sort((a, b) => {
  const topicOrder = TOPICS.findIndex((topic) => topic.id === a.topic) - TOPICS.findIndex((topic) => topic.id === b.topic);
  if (topicOrder) return topicOrder;
  if (a.topic === 'getting-started') return STARTER_ORDER.indexOf(a.slug) - STARTER_ORDER.indexOf(b.slug);
  return a.title.localeCompare(b.title);
});

export const DOC_SECTIONS = TOPICS.map((topic) => ({
  ...topic, pages: DOC_PAGES.filter((page) => page.topic === topic.id),
})).filter((topic) => topic.pages.length > 0);

export const DEFAULT_DOC = 'getting-started/introduction';

export function docHref(slug = DEFAULT_DOC, heading?: string): string {
  return `#/docs/${slug}${heading ? `?heading=${encodeURIComponent(heading)}` : ''}`;
}

export function sourceHref(source: string): string {
  return `${GITHUB_URL}/blob/HEAD/${source.split('/').map(encodeURIComponent).join('/')}`;
}

export function resolveMarkdownHref(href: string, page: DocPage): string {
  if (href.startsWith('#/')) return href;
  if (href.startsWith('#')) return docHref(page.slug, href.slice(1));
  if (/^(https?:|mailto:)/i.test(href)) return href;
  if (/^[a-z][a-z\d+.-]*:/i.test(href) && !/^[a-z]:[/\\]/i.test(href)) return '';

  const [path, fragment] = href.split('#');
  let decodedPath: string;
  try { decodedPath = decodeURIComponent(path); } catch { decodedPath = path; }
  const linkedPage = DOC_PAGES.find((doc) =>
    doc.filename === decodedPath || doc.filename === decodedPath.replace(/^\.\//, ''),
  );
  if (linkedPage) return docHref(linkedPage.slug, fragment);

  // Older guides use local Windows paths and ../Engine paths; make those useful on the web.
  const localPath = decodedPath.replace(/\\/g, '/').replace(/^.*\/obsidian\//i, '');
  const line = localPath.match(/:(\d+)$/)?.[1];
  const cleanPath = localPath.replace(/:\d+$/, '');
  const rootPath = cleanPath.replace(/^(?:\.\.\/)+(Engine|Editor|Vista|HelperSuite|ContentPipeline)\//, '$1/');
  if (/^(Engine|Editor|Vista|HelperSuite|ContentPipeline)\//.test(rootPath)) {
    return `${sourceHref(rootPath)}${line ? `#L${line}` : fragment ? `#${fragment}` : ''}`;
  }
  if (cleanPath.endsWith('.excalidraw')) return sourceHref(`Docs/excalidraw/${cleanPath.split('/').pop()}`);
  const url = new URL(cleanPath, `https://repository.local/${page.source}`);
  return `${sourceHref(url.pathname.slice(1))}${fragment ? `#${fragment}` : ''}`;
}
