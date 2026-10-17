import { useCallback, useEffect, useMemo, useState } from 'react';
import { apiRequest } from './api/client';
import { Portal, Failure } from './Portal';
import { Workspace } from './components/Workspace';
import { createDemoRequest } from './demo/demo';
export interface Session { isAuthenticated: boolean; roles: ('Admin' | 'Landlord' | 'Tenant')[] }
export default function App() {
  const [session, setSession] = useState<Session>();
  const [error, setError] = useState<unknown>();
  const [attempt, setAttempt] = useState(0);
  const [route, setRoute] = useState(location.pathname + location.search);
  const demo = route.split('?')[0] === '/app/demo' || route.startsWith('/app/demo/');
  const demoRequest = useMemo(createDemoRequest, []);
  const clearSession = useCallback(() => { document.title = 'Tenant portal · RentRight'; setSession(undefined); }, []);
  const refresh = useCallback(() => { clearSession(); setError(undefined); setAttempt(v => v + 1); }, [clearSession]);
  const accessLost = useCallback((reason: unknown) => { clearSession(); setError(reason); }, [clearSession]);
  function navigate(path: string) { history.pushState(null, '', path); setRoute(path); refresh(); }
  useEffect(() => {
    if (demo) return;
    const focus = () => refresh();
    const pop = () => { setRoute(location.pathname + location.search); refresh(); };
    const visibility = () => { if (document.visibilityState === 'hidden') clearSession(); else refresh(); };
    window.addEventListener('focus', focus);
    window.addEventListener('popstate', pop);
    document.addEventListener('visibilitychange', visibility);
    return () => { window.removeEventListener('focus', focus); window.removeEventListener('popstate', pop); document.removeEventListener('visibilitychange', visibility); };
  }, [demo, clearSession, refresh]);
  useEffect(() => {
    if (!demo) return;
    const pop = () => { setRoute(location.pathname + location.search); refresh(); };
    window.addEventListener('popstate', pop);
    return () => window.removeEventListener('popstate', pop);
  }, [demo, refresh]);
  useEffect(() => {
    if (demo) return;
    const controller = new AbortController();
    void apiRequest<Session>('/api/v1/session', { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) setSession(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(reason); });
    return () => controller.abort();
  }, [attempt, demo]);
  const tenant = session?.isAuthenticated === true && session.roles.includes('Tenant');
  return <Workspace route={route} navigate={navigate} demo={demo} showTenantNavigation={demo || tenant}>
    {demo ? <Portal key={`${route}:${attempt}`} route={route} navigate={navigate} onAccessLost={accessLost} request={demoRequest} basePath="/app/demo" demo />
      : error ? <div className="welcome-state"><p className="eyebrow">LET’S GET YOU HOME</p><h1>Tenant portal</h1><Failure error={error} retry={refresh} /></div>
        : !session ? <div className="loading-state" role="status"><span className="loading-ring" />Loading your session…</div>
          : !session.isAuthenticated ? <div className="welcome-state"><p className="eyebrow">WELCOME TO RENTRIGHT</p><h1>A little more at home.</h1><p>Sign in to access your RentRight account.</p><a className="button button-primary" href="/Identity/Account/Login?returnUrl=%2Fapp%2F">Sign in</a><a className="text-link" href="/app/demo/">Explore the interactive demo →</a></div>
            : !tenant ? <div className="welcome-state"><h1>Tenant access required</h1><p>This portal is available to tenants. Use your dashboard to manage your account.</p><a className="button button-primary" href="/Dashboard">Open dashboard</a></div>
              : <Portal key={`${route}:${attempt}`} route={route} navigate={navigate} onAccessLost={accessLost} />}
  </Workspace>;
}
