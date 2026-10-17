import { useEffect, useState } from 'react';
import { apiRequest } from './api/client';
import { Portal, Failure } from './Portal';
export interface Session { isAuthenticated: boolean; roles: ('Admin' | 'Landlord' | 'Tenant')[] }
export default function App() {
  const [session, setSession] = useState<Session>();
  const [error, setError] = useState<unknown>();
  const [attempt, setAttempt] = useState(0);
  const [route, setRoute] = useState(location.pathname + location.search);
  function clearSession() { document.title = 'Tenant portal · RentRight'; setSession(undefined); }
  function refresh() { clearSession(); setError(undefined); setAttempt(v => v + 1); }
  function navigate(path: string) {
    history.pushState(null, '', path); setRoute(path); refresh();
  }
  useEffect(() => {
    const focus = () => refresh();
    const pop = () => { setRoute(location.pathname + location.search); refresh(); };
    const visibility = () => { if (document.visibilityState === 'hidden') clearSession(); else refresh(); };
    window.addEventListener('focus', focus);
    window.addEventListener('popstate', pop);
    document.addEventListener('visibilitychange', visibility);
    return () => { window.removeEventListener('focus', focus); window.removeEventListener('popstate', pop); document.removeEventListener('visibilitychange', visibility); };
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    void apiRequest<Session>('/api/v1/session', { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) setSession(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(reason); });
    return () => controller.abort();
  }, [attempt]);
  return <>
    <a href="#main">Skip to content</a>
    <header><p className="brand">RentRight · Tenant portal</p><nav aria-label="Main navigation">
      <a href="/">RentRight home</a><a href="/Dashboard">Dashboard</a><a href="/Identity/Account/Manage">My account</a>
    </nav></header>
    <main id="main" tabIndex={-1}>
      {error ? <><h1>Tenant portal</h1><Failure error={error} retry={refresh} /></> : !session ? <p role="status">Loading your session…</p> : !session.isAuthenticated
        ? <><h1>Sign in</h1><p>Sign in to access your RentRight account.</p><a href="/Identity/Account/Login?returnUrl=%2Fapp%2F">Sign in</a></>
        : !session.roles.includes('Tenant') ? <><h1>Tenant access required</h1><p>This portal is available to tenants. Use your dashboard to manage your account.</p></>
          : <Portal key={`${route}:${attempt}`} route={route} navigate={navigate} onAccessLost={reason => { clearSession(); setError(reason); }} />}
    </main>
  </>;
}
