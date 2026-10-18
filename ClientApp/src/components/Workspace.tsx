import { useEffect, useRef, useState } from 'react';
import type { MouseEvent, ReactNode } from 'react';
import { Icon } from './Icon';

export function Workspace({ children, route, navigate, showTenantNavigation, demo = false, manager = false, admin = false, authenticated = false, onSignOut, signingOut = false }: {
  children: ReactNode; route: string; navigate: (path: string) => void; showTenantNavigation: boolean; demo?: boolean;
  manager?: boolean; admin?: boolean; authenticated?: boolean; onSignOut?: () => void; signingOut?: boolean;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const menuButton = useRef<HTMLButtonElement>(null);
  const closeButton = useRef<HTMLButtonElement>(null);
  const restoreFocus = useRef(false);
  function closeMenu() { restoreFocus.current = true; setMenuOpen(false); }
  useEffect(() => {
    const resize = () => { if (window.innerWidth > 720) setMenuOpen(false); };
    window.addEventListener('resize', resize);
    return () => window.removeEventListener('resize', resize);
  }, []);
  useEffect(() => {
    if (!menuOpen) {
      if (restoreFocus.current) { menuButton.current?.focus(); restoreFocus.current = false; }
      return;
    }
    closeButton.current?.focus();
    const previous = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => { document.body.style.overflow = previous; };
  }, [menuOpen]);
  const base = demo ? '/app/demo' : manager && !showTenantNavigation ? '/app/manage' : '/app';
  const path = route.split('?')[0]?.replace(/\/$/, '');
  const workspaceLabel = manager ? 'Property workspace' : 'Resident workspace';
  const sectionLabel = path?.startsWith('/app/admin') ? 'Administration' : path?.includes('/maintenance') || path?.includes('/requests') ? 'Maintenance' : path?.includes('/tenancies') ? 'Tenancies' : path?.includes('/properties') ? 'Properties' : 'Overview';
  function follow(event: MouseEvent<HTMLDivElement>) {
    const link = (event.target as HTMLElement).closest('a');
    if (link && !link.hash && !link.target && !link.hasAttribute('download') && link.origin === location.origin && (link.pathname === '/app' || link.pathname.startsWith('/app/'))
      && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey && event.button === 0) {
      event.preventDefault(); setMenuOpen(false); navigate(link.pathname + link.search);
    }
  }
  return <div className="workspace" onClick={follow}>
    <a className="skip-link" href="#main">Skip to content</a>
    <aside id="workspace-navigation" className={`sidebar ${menuOpen ? 'sidebar-open' : ''}`} role={menuOpen ? 'dialog' : undefined} aria-modal={menuOpen ? true : undefined} aria-label={menuOpen ? 'Workspace navigation' : undefined} onKeyDown={event => {
      if (!menuOpen) return;
      if (event.key === 'Escape') { event.preventDefault(); closeMenu(); }
      if (event.key === 'Tab') {
        const controls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('a[href], button'));
        const first = controls[0], last = controls[controls.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
      }
    }}>
      <a className="wordmark" href={`${base}/`} aria-label="RentRight overview"><span className="brand-mark"><Icon name="home" /></span>rentright<span className="brand-period">.</span></a>
      <button ref={closeButton} className="mobile-close icon-button" aria-label="Close navigation" onClick={closeMenu}><Icon name="close" /></button>
      <p className="sidebar-caption">A little more at home.</p>
      <div className="workspace-label">YOUR WORKSPACE</div>
      {!demo && <nav className="tenant-nav" aria-label="Property navigation"><a href="/app/properties" aria-current={path?.startsWith('/app/properties') ? 'page' : undefined}><Icon name="pin" />Browse properties</a></nav>}
      {!demo && manager && <nav className="tenant-nav manager-nav" aria-label="Management navigation">
        <a href="/app/manage" aria-current={path === '/app/manage' ? 'page' : undefined}><Icon name="grid" />Management overview</a>
        <a href="/app/manage/properties" aria-current={path?.startsWith('/app/manage/properties') ? 'page' : undefined}><Icon name="home" />Manage properties</a>
        <a href="/app/manage/tenancies" aria-current={path?.startsWith('/app/manage/tenancies') ? 'page' : undefined}><Icon name="calendar" />Tenancies</a>
        <a href="/app/manage/maintenance" aria-current={path?.startsWith('/app/manage/maintenance') ? 'page' : undefined}><Icon name="tool" />Maintenance queue</a>
        {admin && <a href="/app/admin/users" aria-current={path === '/app/admin/users' ? 'page' : undefined}><Icon name="shield" />User roles</a>}
      </nav>}
      {showTenantNavigation && <nav id="tenant-navigation" className="tenant-nav" aria-label="Tenant navigation">
        <a href={`${base}/`} aria-current={path === base ? 'page' : undefined}><Icon name="grid" />Overview</a>
        <a href={`${base}/requests`} aria-current={path === `${base}/requests` || /\/requests\/\d+$/.test(path ?? '') ? 'page' : undefined}><Icon name="requests" />Maintenance requests</a>
        <a href={`${base}/requests/new`} aria-current={path === `${base}/requests/new` ? 'page' : undefined}><Icon name="plus" />New request</a>
        {!demo && <a href="/app/tenancies/accept" aria-current={path === '/app/tenancies/accept' ? 'page' : undefined}><Icon name="calendar" />Accept invitation</a>}
      </nav>}
      <div className="sidebar-bottom">
        <div className="sidebar-note"><span className="note-icon"><Icon name={demo ? 'shield' : 'home'} /></span><strong>{demo ? 'Make yourself at home.' : 'Your home, in one place.'}</strong><p>{demo ? 'Explore the resident experience with fictional data. Nothing here affects a real tenancy.' : 'Keep track of your rental and get the small things sorted.'}</p>{demo && <span className="demo-caption">Interactive demo · resets on reload</span>}</div>
        <nav className="utility-nav" aria-label="Main navigation">
          {demo ? <a href="https://github.com/kevinasramoska/RentRight" target="_blank" rel="noreferrer"><Icon name="arrow" />View source code</a> : authenticated ? <><a href="/Identity/Account/Manage"><Icon name="shield" />My account</a><button className="utility-button" onClick={onSignOut} disabled={signingOut}><Icon name="logout" />{signingOut ? 'Signing out…' : 'Sign out'}</button></> : <a href="/Identity/Account/Login?returnUrl=%2Fapp%2F">Account sign-in</a>}
        </nav>
        <div className="resident-profile"><span className="avatar">{demo ? 'JD' : 'R'}</span><div><strong>{demo ? 'Jamie Davis' : admin ? 'Administrator' : manager ? 'Landlord' : 'Your account'}</strong><span>{demo ? 'Demo resident' : manager ? 'Property workspace' : 'Resident workspace'}</span></div><span className="profile-dot" /></div>
      </div>
    </aside>
    <div className="workspace-body" inert={menuOpen}>
      <header className="topbar"><div className="topbar-location"><button ref={menuButton} className="mobile-menu icon-button" aria-label="Open navigation" aria-expanded={menuOpen} aria-controls="workspace-navigation" onClick={() => setMenuOpen(true)}><Icon name="menu" /></button><span className="mobile-brand">rentright<span className="brand-period">.</span></span><Icon name="home" /><span>{workspaceLabel}</span><span className="breadcrumb-divider">/</span><span className="topbar-current">{sectionLabel}</span></div>
        <div className="topbar-meta">{demo ? <span className="demo-badge"><span />Portfolio demo</span> : <span className="secure-label"><Icon name="shield" />{manager ? 'Property portal' : 'Resident portal'}</span>}<span className="topbar-avatar" aria-hidden="true">{demo ? 'JD' : 'R'}</span></div>
      </header>
      <main id="main" className="main-content" tabIndex={-1}>{children}</main>
      <footer className="workspace-footer"><span>rentright<span className="brand-period">.</span></span><span>A better place to call home.{demo && ' · Synthetic demo data'}</span></footer>
    </div>
    {menuOpen && <button className="menu-backdrop" tabIndex={-1} aria-label="Dismiss navigation" onClick={closeMenu} />}
  </div>;
}
