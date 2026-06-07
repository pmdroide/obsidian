import { useEffect, useState, type ReactNode } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import {
  Search,
  Rocket,
  Armchair,
  Code2,
  Pencil,
  PackageOpen,
  Boxes,
  Image as ImageIcon,
  Network,
  Zap,
  Monitor,
  Gamepad2,
  Workflow,
  Clapperboard,
  Disc,
  Volume2,
  Joystick,
  Settings,
  PersonStanding,
  ChevronRight,
  Sun,
  Moon,
  Menu,
  X,
  type LucideIcon,
} from 'lucide-react';

// Eagerly load every Markdown file under ./markdown as a raw string.
// Keys look like "./markdown/getting-started/introduction.md".
// Add a new .md file + a sidebar entry below and it just works.
const PAGES = import.meta.glob('./markdown/**/*.md', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

function pageContent(slug: string): string {
  return (
    PAGES[`./markdown/${slug}.md`] ??
    `# Not found\n\nNo Markdown file exists for \`${slug}\`.`
  );
}

type DocPage = {
  label: string;
  slug: string; // path under ./markdown without the .md extension
};

type Section = {
  label: string;
  icon: LucideIcon;
  children: DocPage[];
};

const SIDEBAR_ITEMS: Section[] = [
  {
    label: 'Getting Started',
    icon: Rocket,
    children: [
      { label: 'Introduction', slug: 'getting-started/introduction' },
      { label: 'First Steps', slug: 'getting-started/first-steps' },
      { label: 'Reporting Issues', slug: 'getting-started/reporting-issues' },
    ],
  },
  {
    label: 'Scene',
    icon: Armchair,
    children: [
      { label: 'Overview', slug: 'scene/overview' },
      { label: 'Hierarchy', slug: 'scene/hierarchy' },
      { label: 'GameObjects', slug: 'scene/gameobjects' },
    ],
  },
  {
    label: 'Code',
    icon: Code2,
    children: [
      { label: 'Overview', slug: 'code/overview' },
      { label: 'Components', slug: 'code/components' },
      { label: 'Hotloading', slug: 'code/hotloading' },
    ],
  },
  {
    label: 'Editor',
    icon: Pencil,
    children: [
      { label: 'Overview', slug: 'editor/overview' },
      { label: 'Inspector', slug: 'editor/inspector' },
    ],
  },
  {
    label: 'Exporting Standalone',
    icon: PackageOpen,
    children: [
      { label: 'Overview', slug: 'exporting-standalone/overview' },
      { label: 'Publishing', slug: 'exporting-standalone/publishing' },
    ],
  },
  {
    label: 'Assets',
    icon: Boxes,
    children: [
      { label: 'Overview', slug: 'assets/overview' },
      { label: 'Importing', slug: 'assets/importing' },
    ],
  },
  {
    label: 'Rendering',
    icon: ImageIcon,
    children: [
      { label: 'Overview', slug: 'rendering/overview' },
      { label: 'Materials', slug: 'rendering/materials' },
    ],
  },
  {
    label: 'Networking & Multiplayer',
    icon: Network,
    children: [
      { label: 'Overview', slug: 'networking/overview' },
      { label: 'Networking Basics', slug: 'networking/basics' },
    ],
  },
  {
    label: 'Physics',
    icon: Zap,
    children: [
      { label: 'Overview', slug: 'physics/overview' },
      { label: 'Colliders', slug: 'physics/colliders' },
    ],
  },
  {
    label: 'UI',
    icon: Monitor,
    children: [
      { label: 'Overview', slug: 'ui/overview' },
      { label: 'Razor', slug: 'ui/razor' },
    ],
  },
  {
    label: 'Game Mounts',
    icon: Gamepad2,
    children: [
      { label: 'Overview', slug: 'game-mounts/overview' },
      { label: 'Setup', slug: 'game-mounts/setup' },
    ],
  },
  {
    label: 'ActionGraph',
    icon: Workflow,
    children: [
      { label: 'Overview', slug: 'actiongraph/overview' },
      { label: 'Nodes', slug: 'actiongraph/nodes' },
    ],
  },
  {
    label: 'Movie Maker',
    icon: Clapperboard,
    children: [
      { label: 'Overview', slug: 'movie-maker/overview' },
      { label: 'Timeline', slug: 'movie-maker/timeline' },
    ],
  },
  {
    label: 'Media',
    icon: Disc,
    children: [
      { label: 'Overview', slug: 'media/overview' },
      { label: 'Video', slug: 'media/video' },
    ],
  },
  {
    label: 'Sound',
    icon: Volume2,
    children: [
      { label: 'Overview', slug: 'sound/overview' },
      { label: 'Sound Events', slug: 'sound/sound-events' },
    ],
  },
  {
    label: 'Gameplay',
    icon: Joystick,
    children: [
      { label: 'Overview', slug: 'gameplay/overview' },
      { label: 'Player', slug: 'gameplay/player' },
    ],
  },
  {
    label: 'Services',
    icon: Settings,
    children: [
      { label: 'Overview', slug: 'services/overview' },
      { label: 'Stats & Leaderboards', slug: 'services/stats' },
    ],
  },
  {
    label: 'Animation',
    icon: PersonStanding,
    children: [
      { label: 'Overview', slug: 'animation/overview' },
      { label: 'AnimGraph', slug: 'animation/animgraph' },
    ],
  },
];

const TOP_NAV = ['about', 'games', 'workshop', 'forum', 'learn'];

type Theme = 'dark' | 'light';

function getInitialTheme(): Theme {
  const attr = document.documentElement.getAttribute('data-theme');
  return attr === 'light' ? 'light' : 'dark';
}

const MARKDOWN_COMPONENTS = {
  h1: ({ children }: { children?: ReactNode }) => (
    <h1 className="text-2xl md:text-3xl text-primary font-medium mb-6 md:mb-8">{children}</h1>
  ),
  h2: ({ children }: { children?: ReactNode }) => (
    <h2 className="text-lg md:text-xl text-foreground font-medium mt-6 md:mt-8 mb-3">{children}</h2>
  ),
  h3: ({ children }: { children?: ReactNode }) => (
    <h3 className="text-base md:text-lg text-foreground font-medium mt-6 md:mt-8 mb-3">{children}</h3>
  ),
  p: ({ children }: { children?: ReactNode }) => (
    <p className="text-muted-foreground leading-relaxed mb-3">{children}</p>
  ),
  a: ({ href, children }: { href?: string; children?: ReactNode }) => (
    <a href={href} className="text-primary hover:underline">
      {children}
    </a>
  ),
  ul: ({ children }: { children?: ReactNode }) => (
    <ul className="list-disc pl-6 mb-3 space-y-1 text-muted-foreground leading-relaxed">
      {children}
    </ul>
  ),
  ol: ({ children }: { children?: ReactNode }) => (
    <ol className="list-decimal pl-6 mb-3 space-y-1 text-muted-foreground leading-relaxed">
      {children}
    </ol>
  ),
  blockquote: ({ children }: { children?: ReactNode }) => (
    <blockquote className="border-l-2 border-border pl-4 my-4 text-muted-foreground italic">
      {children}
    </blockquote>
  ),
  code: ({ children }: { children?: ReactNode }) => (
    <code className="bg-card px-1.5 py-0.5 rounded text-sm text-foreground break-words">
      {children}
    </code>
  ),
};

export default function DocsPage() {
  const [activeSlug, setActiveSlug] = useState(
    SIDEBAR_ITEMS[0].children[0].slug
  );
  const [expanded, setExpanded] = useState<Record<string, boolean>>({
    [SIDEBAR_ITEMS[0].label]: true,
  });
  const [theme, setTheme] = useState<Theme>(getInitialTheme);
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

  // Reflect the active theme onto <html> and remember the choice.
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    try {
      localStorage.setItem('theme', theme);
    } catch {
      // Ignore storage failures
    }
  }, [theme]);

  // Prevent background scrolling when mobile sidebar drawer is open
  useEffect(() => {
    if (isMobileMenuOpen) {
      document.body.style.overflow = 'hidden';
    } else {
      document.body.style.overflow = 'unset';
    }
    return () => {
      document.body.style.overflow = 'unset';
    };
  }, [isMobileMenuOpen]);

  const toggleTheme = () =>
    setTheme((prev) => (prev === 'dark' ? 'light' : 'dark'));

  const toggleSection = (label: string) =>
    setExpanded((prev) => ({ ...prev, [label]: !prev[label] }));

  return (
    <div className="size-full min-h-screen flex flex-col bg-background text-foreground overflow-x-hidden">
      {/* Top Bar — Kept 4 direct children to maintain correct justify-between alignment on desktop */}
      <header className="flex items-center justify-between gap-4 px-4 md:px-8 py-4 border-b border-border sticky top-0 bg-background z-30">
        
        {/* 1. Logo & Mobile Hamburger Menu toggle */}
        <div className="flex items-center gap-3">
          <button 
            className="md:hidden text-muted-foreground hover:text-foreground transition-colors"
            onClick={() => setIsMobileMenuOpen(true)}
            aria-label="Open Menu"
          >
            <Menu className="w-6 h-6" />
          </button>
          <img
            src="/logo.png"
            alt="Obsidian"
            className="brand-logo w-10 h-10 shrink-0 select-none"
          />
        </div>

        {/* 2. Nav links (Hidden on mobile/tablet screens) */}
        <nav className="hidden lg:flex items-center gap-6">
          {TOP_NAV.map((item) => (
            <button
              key={item}
              className="text-sm text-muted-foreground hover:text-primary transition-colors"
            >
              {item}
            </button>
          ))}
        </nav>

        {/* 3. Search (Hidden on smallest mobile devices) */}
        <div className="relative hidden sm:block">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
          <input
            type="text"
            placeholder="Find Docs.."
            className="w-40 md:w-56 bg-input border border-border rounded-lg pl-9 pr-3 py-2 text-sm text-foreground placeholder:text-muted-foreground outline-none focus:border-primary transition-colors"
          />
        </div>

        {/* 4. Theme toggle (Always visible, stays perfectly pinned to the right edge) */}
        <button
          onClick={toggleTheme}
          aria-label={`Switch to ${theme === 'dark' ? 'light' : 'dark'} theme`}
          title={`Switch to ${theme === 'dark' ? 'light' : 'dark'} theme`}
          className="flex items-center justify-center w-10 h-10 rounded-lg bg-card border border-border hover:bg-accent transition-colors shrink-0"
        >
          {theme === 'dark' ? (
            <Sun className="w-5 h-5 text-muted-foreground" />
          ) : (
            <Moon className="w-5 h-5 text-muted-foreground" />
          )}
        </button>
      </header>

      {/* Body Layout */}
      <div className="flex-1 flex max-w-7xl w-full mx-auto px-4 md:px-8 py-6 md:py-10 gap-6 lg:gap-12 relative">
        
        {/* Mobile Backdrop Overlay Blur */}
        {isMobileMenuOpen && (
          <div 
            className="fixed inset-0 bg-background/80 backdrop-blur-sm z-40 md:hidden"
            onClick={() => setIsMobileMenuOpen(false)}
          />
        )}

        {/* Sidebar Navigation */}
        <aside 
          className={`
            fixed inset-y-0 left-0 z-50 w-64 bg-background border-r border-border p-6 overflow-y-auto transform transition-transform duration-300 ease-in-out
            md:relative md:z-0 md:p-0 md:border-none md:translate-x-0 md:overflow-visible shrink-0
            ${isMobileMenuOpen ? 'translate-x-0 shadow-2xl' : '-translate-x-full'}
          `}
        >
          {/* Mobile Close Bar inside Menu Drawer */}
          <div className="flex items-center justify-between mb-6 md:hidden">
            <span className="font-semibold text-foreground">Documentation</span>
            <button 
              onClick={() => setIsMobileMenuOpen(false)}
              className="text-muted-foreground hover:text-foreground"
            >
              <X className="w-5 h-5" />
            </button>
          </div>

          <nav className="space-y-1">
            {SIDEBAR_ITEMS.map(({ label, icon: Icon, children }) => {
              const isOpen = expanded[label] ?? false;
              const hasActiveChild = children.some((c) => c.slug === activeSlug);
              return (
                <div key={label}>
                  <button
                    onClick={() => toggleSection(label)}
                    aria-expanded={isOpen}
                    className={`w-full flex items-center gap-3 px-2 py-1.5 rounded text-sm transition-colors ${
                      hasActiveChild
                        ? 'text-primary'
                        : 'text-foreground hover:bg-accent'
                    }`}
                  >
                    <ChevronRight
                      className={`w-3 h-3 shrink-0 transition-transform ${
                        isOpen ? 'rotate-90' : ''
                      } ${
                        hasActiveChild ? 'text-primary' : 'text-muted-foreground'
                      }`}
                    />
                    <Icon className="w-4 h-4 shrink-0 text-primary" />
                    <span className="text-left font-medium md:font-normal">{label}</span>
                  </button>

                  {isOpen && (
                    <div className="ml-[1.375rem] mt-1 mb-1 flex flex-col gap-1 border-l border-border pl-3">
                      {children.map((child) => {
                        const isActive = child.slug === activeSlug;
                        return (
                          <button
                            key={child.slug}
                            onClick={() => {
                              setActiveSlug(child.slug);
                              setIsMobileMenuOpen(false); // Closes the drawer on selection
                            }}
                            className={`text-left px-2 py-1.5 md:py-1 rounded text-sm transition-colors ${
                              isActive
                                ? 'text-primary bg-accent font-medium'
                                : 'text-muted-foreground hover:text-foreground hover:bg-accent'
                            }`}
                          >
                            {child.label}
                          </button>
                        );
                      })}
                    </div>
                  )}
                </div>
              );
            })}
          </nav>
        </aside>

        {/* Main Content Pane */}
        <main className="flex-1 min-w-0 overflow-hidden">
          <ReactMarkdown
            remarkPlugins={[remarkGfm]}
            components={MARKDOWN_COMPONENTS}
          >
            {pageContent(activeSlug)}
          </ReactMarkdown>
        </main>
      </div>

      {/* Footer */}
      <footer className="border-t border-border mt-auto py-6">
        <div className="max-w-7xl w-full mx-auto px-4 md:px-8 flex flex-col sm:flex-row items-center justify-between gap-4 text-xs text-muted-foreground">
          <p>&copy; {new Date().getFullYear()} Obsidian. All rights reserved.</p>
          
          {/* Optional: Extra small links on the right side of the footer */}
          <div className="flex items-center gap-4">
            <button className="hover:text-foreground transition-colors">Privacy Policy</button>
            <button className="hover:text-foreground transition-colors">Terms of Service</button>
          </div>
        </div>
      </footer>
    </div>
  );
}