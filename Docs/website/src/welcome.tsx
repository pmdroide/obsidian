import { useEffect } from 'react';
import { ArrowDown, ArrowRight, BookOpen, Boxes, Code2, Monitor } from 'lucide-react';
import Github from './github-icon';
import { DOC_PAGES, docHref, GITHUB_URL } from './docs';

const QUICK_STARTS = [
  { icon: Boxes, number: '01', title: 'Build your world', description: 'Scenes, GameObjects, components, and the flow that brings them together.', slug: 'scene/game-flow', label: 'Explore scenes' },
  { icon: Code2, number: '02', title: 'Make it move', description: 'Write C# behaviours. Connect input, animation, audio, and gameplay.', slug: 'code/script-behaviours', label: 'Start scripting' },
  { icon: Monitor, number: '03', title: 'Shape the experience', description: 'Work in Anvil, light your scenes, and create interfaces with Vista UI.', slug: 'editor/architecture', label: 'Meet the editor' },
];

export default function WelcomePage() {
  useEffect(() => { document.title = 'Obsidian · An open source C# game engine'; }, []);
  return (
    <main id="main-content" className="welcome-main" tabIndex={-1}>
      <section className="hero">
        <div className="hero-copy">
          <p className="eyebrow"><span className="status-dot" /> OPEN SOURCE. OPEN POSSIBILITIES.</p>
          <h1>Your ideas.<br />A world of <span>possibilities.</span></h1>
          <p className="hero-description">Meet Obsidian. A C# game engine with a hands-on editor, a flexible rendering pipeline, and room to make something your own.</p>
          <div className="hero-actions">
            <a className="button button-primary" href={docHref()}><BookOpen size={18} /> Explore the docs <ArrowRight size={18} /></a>
            <a className="button button-secondary" href={GITHUB_URL} target="_blank" rel="noreferrer"><Github size={18} /> View on GitHub</a>
          </div>
          <div className="hero-details"><span>C# / .NET</span><span>Powered by MonoGame</span><span>MIT licensed</span></div>
        </div>
        <div className="hero-art" aria-hidden="true">
          <div className="art-grid" />
          <div className="art-orbit orbit-one" /><div className="art-orbit orbit-two" />
          <div className="crystal-glow" />
          <svg className="hero-crystal" viewBox="0 0 400 420" fill="none">
            <defs>
              <linearGradient id="crystal-left" x1="78" y1="170" x2="236" y2="370" gradientUnits="userSpaceOnUse"><stop stopColor="#c7b9ff" /><stop offset="1" stopColor="#514389" /></linearGradient>
              <linearGradient id="crystal-right" x1="220" y1="70" x2="285" y2="370" gradientUnits="userSpaceOnUse"><stop stopColor="#a68bff" /><stop offset="1" stopColor="#292342" /></linearGradient>
              <linearGradient id="crystal-front" x1="164" y1="155" x2="247" y2="340" gradientUnits="userSpaceOnUse"><stop stopColor="#8064d9" /><stop offset="1" stopColor="#342849" /></linearGradient>
            </defs>
            <path d="M217 42 296 142 316 274 225 378 88 303 109 159Z" fill="url(#crystal-left)" stroke="#c6b5ff" strokeOpacity=".5" />
            <path d="m217 42 12 157 67-57Z" fill="#d7c9ff" fillOpacity=".8" />
            <path d="m217 42-108 117 68 40 52 0Z" fill="#aa91e4" />
            <path d="m296 142 20 132-91 104 4-179Z" fill="url(#crystal-right)" />
            <path d="m109 159-21 144 89-104Z" fill="#c9bbec" fillOpacity=".5" />
            <path d="m177 199-89 104 137 75 4-179Z" fill="url(#crystal-front)" />
            <path d="m217 42 12 157-4 179M109 159l68 40h52l67-57M177 199l48 179" stroke="#e1d4ff" strokeOpacity=".25" />
          </svg>
          <span className="art-label art-label-top"><span /> THE BUILD STARTS HERE</span>
          <span className="art-label art-label-bottom">OBSIDIAN ENGINE <span>↗</span></span>
        </div>
      </section>
      <div className="welcome-divider"><span>A little curiosity. A lot to create.</span><a href="#explore" onClick={(event) => {
        event.preventDefault();
        document.getElementById('explore')?.scrollIntoView({ behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' });
      }}>Find your starting point <ArrowDown size={15} /></a></div>
      <section id="explore" className="explore-section">
        <div className="section-intro"><div><p className="eyebrow">FROM IDEA TO INTERACTIVE</p><h2>Make your next move.</h2></div><p>Learn the pieces.<br />Then put them together.</p></div>
        <div className="quick-start-grid">
          {QUICK_STARTS.map(({ icon: Icon, number, title, description, slug, label }) => (
            <a key={slug} className="quick-start-card" href={docHref(slug)}>
              <div className="card-top"><Icon size={24} strokeWidth={1.5} /><span>{number}</span></div>
              <h3>{title}</h3><p>{description}</p><span className="card-link">{label}<ArrowRight size={17} /></span>
            </a>
          ))}
        </div>
      </section>
      <section className="source-callout">
        <div className="source-icon"><Github size={30} /></div>
        <div><p className="eyebrow">BUILT IN THE OPEN</p><h2>Read it. Build it. Make it yours.</h2><p>{DOC_PAGES.filter((page) => page.source.startsWith('Docs/markdown/')).length} engine guides, the full source, and a place for your contributions.</p></div>
        <a className="button button-secondary" href={GITHUB_URL} target="_blank" rel="noreferrer">Explore the repository <ArrowRight size={17} /></a>
      </section>
    </main>
  );
}
