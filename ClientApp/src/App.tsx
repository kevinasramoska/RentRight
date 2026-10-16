import { useEffect, useState } from 'react';
import { apiRequest } from './api/client';
import { EmptyState, ErrorState, LoadingState } from './components/States';
export interface Session { isAuthenticated: boolean; roles: ('Admin' | 'Landlord' | 'Tenant')[] }
export default function App() {
  const [session, setSession] = useState<Session>();
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setFailed(false);
    setSession(undefined);
    void apiRequest<Session>('/api/v1/session', { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) setSession(value); })
      .catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [attempt]);
  return <>
    <a href="#main">Skip to content</a>
    <header><nav aria-label="Main navigation"><a href="/">RentRight home</a> <a href="/app/" aria-current="page">Application</a> {session?.isAuthenticated
      ? <><a href="/Dashboard">Dashboard</a> <a href="/Identity/Account/Manage">My account</a></>
      : <a href="/Identity/Account/Login">Sign in</a>}</nav></header>
    <main id="main" tabIndex={-1}><h1>RentRight</h1>
      {failed ? <ErrorState onRetry={() => setAttempt(value => value + 1)} /> : !session ? <LoadingState /> : session.isAuthenticated
        ? <><p>You’re signed in.</p><EmptyState message="Your existing dashboard is available from the navigation. More application features are coming soon." /></>
        : <EmptyState message="Sign in to access your RentRight account." />}
    </main>
  </>;
}
