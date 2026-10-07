import { lazy, Suspense, useEffect, useState } from 'react';
import { BookOpen, Moon, Sun } from 'lucide-react';
import Github from './github-icon';
import WelcomePage from './welcome';
import { docHref, GITHUB_URL } from './docs';

const DocsPage = lazy(() => import('./devdocs'));

export default function App() {
  const [route, setRoute] = useState(() => window.location.hash.slice(1) || '/');
  const [theme, setTheme] = useState(() => document.documentElement.dataset.theme === 'light' ? 'light' : 'dark');

  useEffect(() => {
    const onHashChange = () => setRoute(window.location.hash.slice(1) || '/');
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try { localStorage.setItem('theme', theme); } catch { /* Storage can be unavailable. */ }
  }, [theme]);

  useEffect(() => {
    window.scrollTo(0, 0);
  }, [route]);

  const isWelcome = route === '/';

  return (
    <div className={isWelcome ? 'site welcome-site' : 'site docs-site'}>
      <a className="skip-link" href="#main-content" onClick={(event) => {
        event.preventDefault();
        document.getElementById('main-content')?.focus();
      }}>Skip to content</a>
      <header className="site-header">
        <div className="header-inner">
          <a href="#/" className="brand" aria-label="Obsidian home">
            <img src={`${import.meta.env.BASE_URL}logo.png`} alt="" className="brand-logo" />
            <span>obsidian<span className="brand-dot">.</span></span>
          </a>
          <nav className="header-nav" aria-label="Main navigation">
            <a href={docHref()} className={!isWelcome ? 'nav-active' : ''}><BookOpen size={16} /><span>Documentation</span></a>
            <a href={GITHUB_URL} target="_blank" rel="noreferrer"><Github size={16} /><span>GitHub</span></a>
            <button className="icon-button theme-button" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}
              aria-label={`Switch to ${theme === 'dark' ? 'light' : 'dark'} theme`}>
              {theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
            </button>
          </nav>
        </div>
      </header>
      {isWelcome ? <WelcomePage /> : <Suspense fallback={<main id="main-content" className="docs-loading" tabIndex={-1} role="status">Loading documentation…</main>}><DocsPage key={route.split('?')[0]} route={route} /></Suspense>}
      <footer className="site-footer">
        <a className="footer-brand" href="#/">obsidian.</a>
        <p>An open source C# game engine.</p>
        <a href={`${GITHUB_URL}/blob/HEAD/LICENSE`} target="_blank" rel="noreferrer">MIT licensed · {new Date().getFullYear()}</a>
      </footer>
    </div>
  );
}
