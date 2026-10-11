import { useEffect, useRef, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { ArrowLeft, ArrowRight, BookOpen, ChevronDown, ChevronRight, ExternalLink, FileText, Menu, Search, X } from 'lucide-react';
import { DEFAULT_DOC, DOC_PAGES, DOC_SECTIONS, docHref, headingId, resolveMarkdownHref, sourceHref } from './docs';

// Give Markdown headings stable, GitHub-style anchors, including repeated headings.
type MarkdownNode = {
  type: string; value?: string; depth?: number;
  children?: MarkdownNode[]; data?: { hProperties?: Record<string, string> };
};
function nodeText(node: MarkdownNode): string {
  return node.value ?? node.children?.map(nodeText).join('') ?? '';
}
function headingAnchors() {
  return (tree: MarkdownNode) => {
    const counts = new Map<string, number>();
    const visit = (node: MarkdownNode) => {
      if (node.type === 'heading') {
        const base = headingId(nodeText(node));
        const count = counts.get(base) ?? 0;
        counts.set(base, count + 1);
        node.data = { ...node.data, hProperties: { ...node.data?.hProperties, id: count ? `${base}-${count}` : base } };
      }
      node.children?.forEach(visit);
    };
    visit(tree);
  };
}
function outline(content: string) {
  const counts = new Map<string, number>();
  return [...content.replace(/^(`{3,}|~{3,})[^\n]*\n[\s\S]*?^\1\s*$/gm, '').matchAll(/^(#{1,3})\s+(.+)$/gm)].map((match) => {
    const title = match[2].replace(/\[([^\]]+)\]\([^)]*\)/g, '$1').replace(/[*`_]/g, '');
    const base = headingId(title);
    const count = counts.get(base) ?? 0;
    counts.set(base, count + 1);
    return { title, depth: match[1].length, id: count ? `${base}-${count}` : base };
  }).filter((heading) => heading.depth > 1);
}

function setPageTitle(title: string) {
  document.title = title;
}

export default function DocsPage({ route }: { route: string }) {
  const [path, query] = route.split('?');
  const slug = path === '/docs' || path === '/docs/' ? DEFAULT_DOC : path.replace(/^\/docs\//, '');
  const page = DOC_PAGES.find((doc) => doc.slug === slug);
  const section = DOC_SECTIONS.find((topic) => topic.id === page?.topic);
  const [expanded, setExpanded] = useState<Record<string, boolean>>({ [section?.id ?? 'getting-started']: true });
  const [search, setSearch] = useState('');
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);
  const menuButton = useRef<HTMLButtonElement>(null);
  const sidebar = useRef<HTMLElement>(null);
  const normalizedSearch = search.trim().toLowerCase();
  const filteredSections = DOC_SECTIONS.map((topic) => ({
    ...topic,
    pages: topic.pages.filter((doc) => !normalizedSearch || `${topic.label} ${doc.title} ${doc.content}`.toLowerCase().includes(normalizedSearch)),
  })).filter((topic) => topic.pages.length);
  const headings = page ? outline(page.content) : [];
  const currentIndex = DOC_PAGES.findIndex((doc) => doc.slug === page?.slug);
  const previous = DOC_PAGES[currentIndex - 1];
  const next = currentIndex >= 0 ? DOC_PAGES[currentIndex + 1] : undefined;

  useEffect(() => {
    setPageTitle(`${page?.title ?? 'Page not found'} · Obsidian Docs`);
    const heading = new URLSearchParams(query).get('heading');
    if (heading) {
      const frame = requestAnimationFrame(() => document.getElementById(heading)?.scrollIntoView());
      return () => cancelAnimationFrame(frame);
    }
  }, [page, query]);

  useEffect(() => {
    if (!isMobileMenuOpen) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const first = sidebar.current?.querySelector<HTMLElement>('button, input, a');
    first?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setIsMobileMenuOpen(false);
        menuButton.current?.focus();
      }
      if (event.key === 'Tab') {
        const items = [...(sidebar.current?.querySelectorAll<HTMLElement>('a, button, input') ?? [])];
        const firstItem = items[0];
        const lastItem = items[items.length - 1];
        if (event.shiftKey && document.activeElement === firstItem) { event.preventDefault(); lastItem?.focus(); }
        else if (!event.shiftKey && document.activeElement === lastItem) { event.preventDefault(); firstItem?.focus(); }
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', onKeyDown);
    };
  }, [isMobileMenuOpen]);

  return (
    <div className="docs-layout">
      <div className="mobile-docs-bar">
        <button ref={menuButton} className="icon-button" onClick={() => setIsMobileMenuOpen(true)} aria-label="Open documentation menu" aria-expanded={isMobileMenuOpen} aria-controls="docs-sidebar"><Menu size={20} /></button>
        <span>{section?.label ?? 'Documentation'}</span><ChevronRight size={14} /><span>{page?.title ?? 'Not found'}</span>
      </div>
      {isMobileMenuOpen && <div className="sidebar-backdrop" onClick={() => { setIsMobileMenuOpen(false); menuButton.current?.focus(); }} />}
      <aside ref={sidebar} id="docs-sidebar" className={`docs-sidebar ${isMobileMenuOpen ? 'is-open' : ''}`} aria-label="Documentation navigation">
        <div className="sidebar-title"><span><BookOpen size={17} /> Documentation</span><button className="icon-button mobile-close" onClick={() => { setIsMobileMenuOpen(false); menuButton.current?.focus(); }} aria-label="Close documentation menu"><X size={20} /></button></div>
        <label className="docs-search"><Search size={16} /><input type="search" placeholder="Search the docs…" aria-label="Search documentation" value={search} onChange={(event) => setSearch(event.target.value)} /></label>
        <nav aria-label="Documentation topics">
          {filteredSections.map(({ id, label, icon: Icon, pages }) => {
            const isOpen = Boolean(normalizedSearch) || expanded[id];
            return <div className="sidebar-section" key={id}>
              <button className={`section-toggle ${section?.id === id ? 'active-section' : ''}`} onClick={() => setExpanded((current) => ({ ...current, [id]: !isOpen }))} aria-expanded={Boolean(isOpen)} aria-controls={`topic-${id}`}><Icon size={17} /><span>{label}</span><ChevronDown size={14} className={isOpen ? '' : 'chevron-closed'} /></button>
              {isOpen && <div id={`topic-${id}`} className="section-pages">{pages.map((doc) => <a key={doc.slug} href={docHref(doc.slug)} aria-current={page?.slug === doc.slug ? 'page' : undefined} className={page?.slug === doc.slug ? 'active-page' : ''} onClick={() => setIsMobileMenuOpen(false)}>{doc.title}</a>)}</div>}
            </div>;
          })}
        </nav>
        {!filteredSections.length && <p className="search-empty" role="status">No guides match “{search}”. Try a different search.</p>}
        {normalizedSearch && <p className="search-count" role="status">{filteredSections.reduce((total, topic) => total + topic.pages.length, 0)} guides found</p>}
        <a className="sidebar-home" href="#/"><ArrowLeft size={14} /> Back to Obsidian</a>
      </aside>
      <main id="main-content" className="docs-content" tabIndex={-1}>
        {page ? <>
          <div className="docs-breadcrumb"><span>Docs</span><ChevronRight size={13} /><span>{section?.label}</span></div>
          <div className="article-meta"><span><FileText size={13} /> ENGINE GUIDE</span><span>{Math.max(1, Math.ceil(page.content.split(/\s+/).length / 220))} min read</span></div>
          <article className="markdown-content">
            <ReactMarkdown remarkPlugins={[remarkGfm, headingAnchors]} urlTransform={(href) => resolveMarkdownHref(href, page)} components={{
              a: ({ href, children }) => <a href={href} {...(href?.startsWith('http') ? { target: '_blank', rel: 'noreferrer' } : {})}>{children}</a>,
              table: ({ children }) => <div className="table-scroll"><table>{children}</table></div>,
            }}>{page.content.startsWith('# ') ? page.content : `# ${page.title}\n\n${page.content}`}</ReactMarkdown>
          </article>
          <div className="article-source"><a href={sourceHref(page.source)} target="_blank" rel="noreferrer">View this guide on GitHub <ExternalLink size={14} /></a><span>{page.source}</span></div>
          <nav className="article-pagination" aria-label="Previous and next guide">
            {previous ? <a href={docHref(previous.slug)}><ArrowLeft size={16} /><div><span>Previous guide</span><strong>{previous.title}</strong></div></a> : <div />}
            {next && <a href={docHref(next.slug)}><div><span>Next guide</span><strong>{next.title}</strong></div><ArrowRight size={16} /></a>}
          </nav>
        </> : <div className="not-found"><p className="eyebrow">404 / GUIDE NOT FOUND</p><h1>This page hasn’t been written.</h1><p>Find a current guide in the sidebar, or start with an introduction to Obsidian.</p><a className="button button-primary" href={docHref()}>Open the docs <ArrowRight size={17} /></a></div>}
      </main>
      {page && <aside className="docs-outline" aria-label="On this page"><p className="outline-title">ON THIS PAGE</p><nav>{headings.map((heading) => <a href={docHref(page.slug, heading.id)} className={heading.depth === 3 ? 'outline-subheading' : ''} key={heading.id}>{heading.title}</a>)}</nav><a className="outline-source" href={sourceHref(page.source)} target="_blank" rel="noreferrer">View source <ExternalLink size={13} /></a></aside>}
    </div>
  );
}
