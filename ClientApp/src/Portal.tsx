import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { ApiError, apiRequest } from './api/client';
import type { ApiPage } from './api/client';
import type { Dashboard, Detail, MaintenanceItem, RecentRequest } from './api/tenant';

const requests = '/api/v1/maintenance-requests';
const descriptionError = 'Enter a description between 10 and 2000 characters after trimming.';
export function errorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) return 'We couldn’t connect. Check your connection and try again.';
  switch (error.status) {
    case 400: return 'Check the form and try again.';
    case 401: return 'Your session has ended. Sign in again.';
    case 403: return 'You don’t have access to this tenant resource.';
    case 404: return 'This request is unavailable or no longer accessible.';
    case 409: return error.problem.code === 'no_active_tenancy' ? 'You need a currently active tenancy to submit a request.' : 'Your tenancy or request has changed. Review the overview before trying again.';
    case 429: return 'Too many requests. Please wait before trying again.';
    case 503: return 'The service is temporarily unavailable. Please try again later.';
    default: return 'We couldn’t complete the request. Please try again.';
  }
}
export function Failure({ error, retry }: { error: unknown; retry: () => void }) {
  return <div role="alert"><p>{errorMessage(error)}</p>{error instanceof ApiError && error.status === 401
    ? <a href="/Identity/Account/Login?returnUrl=%2Fapp%2F">Sign in</a> : <button onClick={retry}>Try again</button>}</div>;
}
function utc(value: string) { return new Date(value).toLocaleString('en-IE', { timeZone: 'UTC', dateStyle: 'medium', timeStyle: 'short' }) + ' UTC'; }
function status(value: string) { return value === 'InProgress' ? 'In progress' : value; }
function NoTenancy() { return <p>You do not have a currently active tenancy. <a href="/Tenancies/Accept">Accept a valid invitation</a> or contact your landlord.</p>; }
function RequestList({ items }: { items: RecentRequest[] }) {
  return items.length === 0 ? <p>No maintenance requests to show.</p> : <ul className="requests">{items.map(item => <li key={item.id}>
    <a href={`/app/requests/${item.id}`}>Request #{item.id} · {item.propertyTitle}</a><p>{status(item.status)} · <time dateTime={item.submittedUtc}>{utc(item.submittedUtc)}</time></p>
    {'priority' in item && <p>Priority: {String(item.priority)}</p>}
  </li>)}</ul>;
}
export function Portal({ route, navigate, onAccessLost }: { route: string; navigate: (path: string) => void; onAccessLost: (error: unknown) => void }) {
  const url = new URL(route, location.origin);
  const path = url.pathname.replace(/\/$/, '');
  const isOverview = path === '/app';
  const isList = path === '/app/requests';
  const isNew = path === '/app/requests/new';
  const id = /^\/app\/requests\/([1-9]\d*)$/.exec(path)?.[1];
  const parsedPage = Number(url.searchParams.get('page') ?? 1);
  const page = Number.isInteger(parsedPage) && parsedPage >= 1 && parsedPage <= 10000 ? parsedPage : 1;
  const title = isOverview ? 'Overview' : isList ? 'Maintenance requests' : isNew ? 'New maintenance request' : id ? `Request #${id}` : 'Page not found';
  const [data, setData] = useState<Dashboard | ApiPage<MaintenanceItem> | Detail>();
  const [error, setError] = useState<unknown>();
  const [attempt, setAttempt] = useState(0);
  const heading = useRef<HTMLHeadingElement>(null);
  const endpoint = isOverview || isNew ? '/api/v1/tenant-dashboard' : isList ? `${requests}?page=${page}&pageSize=10` : id ? `${requests}/${id}` : undefined;
  useEffect(() => { document.title = `${title} · RentRight`; heading.current?.focus(); }, [title]);
  useEffect(() => {
    const controller = new AbortController();
    if (endpoint) void apiRequest<Dashboard | ApiPage<MaintenanceItem> | Detail>(endpoint, { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) setData(value); })
      .catch(reason => {
        if (controller.signal.aborted) return;
        if (reason instanceof ApiError && [401, 403].includes(reason.status)) onAccessLost(reason);
        setError(reason);
      });
    return () => controller.abort();
  }, [endpoint, attempt, onAccessLost]);
  const retry = () => { setData(undefined); setError(undefined); setAttempt(v => v + 1); };
  return <div onClick={event => {
    const link = (event.target as HTMLElement).closest('a');
    if (link && !link.hash && link.origin === location.origin && (link.pathname === '/app' || link.pathname.startsWith('/app/')) && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey && event.button === 0) {
      event.preventDefault(); navigate(link.pathname + link.search);
    }
  }}>
    <nav aria-label="Tenant navigation"><a href="/app/" aria-current={isOverview ? 'page' : undefined}>Overview</a><a href="/app/requests" aria-current={isList ? 'page' : undefined}>Maintenance requests</a><a href="/app/requests/new" aria-current={isNew ? 'page' : undefined}>New request</a></nav>
    <h1 tabIndex={-1} ref={heading}>{title}</h1>
    {!endpoint ? <p>Choose a page from the tenant navigation.</p> : error ? <Failure error={error} retry={retry} /> : !data ? <p role="status">Loading…</p> : <>
      {isOverview && 'tenancies' in data && <>
        <p className="counts"><strong>{data.counts.open}</strong> open requests · <strong>{data.counts.recent}</strong> submitted in the last 30 days</p>
        <h2>Your active tenancy</h2>{data.tenancies.length === 0 ? <NoTenancy /> : data.tenancies.map((t, i) => <section className="card" key={i}><h3>{t.propertyTitle}</h3><p>{t.propertyAddress}</p><p><time dateTime={t.startDate}>{t.startDate}</time> – {t.endDate ?? 'Open-ended'}</p></section>)}
        <h2>Recent requests</h2><RequestList items={data.recentMaintenanceRequests} />
      </>}
      {isList && 'items' in data && <><p role="status">Page {data.page} of {data.totalPages} · {data.totalCount} requests</p><RequestList items={data.items} />{data.totalCount === 0 && <p>Only requests for your currently active tenancy appear here. Check <a href="/app/">Overview</a> for your tenancy.</p>}
        <nav aria-label="Request pages">{data.page > 1 && <a href={`/app/requests?page=${data.page - 1}`}>Previous page</a>}{data.page < data.totalPages && <a href={`/app/requests?page=${data.page + 1}`}>Next page</a>}</nav></>}
      {id && 'request' in data && <><section className="card"><h2>{data.request.propertyTitle}</h2><p>{data.request.propertyAddress}</p><p className="text">{data.request.description}</p><p>Status: {status(data.request.status)} · Priority: {data.request.priority}</p><p>Submitted: <time dateTime={data.request.submittedUtc}>{utc(data.request.submittedUtc)}</time></p><p>Last updated: <time dateTime={data.request.lastUpdatedUtc}>{utc(data.request.lastUpdatedUtc)}</time></p></section>
        <h2>Status timeline</h2>{data.history.length === 0 ? <p>No status history is available.</p> : <ol className="timeline">{[...data.history].sort((a, b) => Date.parse(a.occurredUtc) - Date.parse(b.occurredUtc)).map((event, i) => <li key={i}><p>{event.previousStatus ? `${status(event.previousStatus)} → ` : ''}{status(event.status)}</p><time dateTime={event.occurredUtc}>{utc(event.occurredUtc)}</time>{event.isLegacyBaseline && <p>Legacy baseline: the state observed when history recording began, not a recorded transition.</p>}{event.note && <p className="text">{event.note.slice(0, 1000)}</p>}</li>)}</ol>}</>}
      {isNew && 'tenancies' in data && (data.tenancies.length === 0 ? <NoTenancy /> : data.tenancies.length > 1 ? <p>More than one active tenancy is associated with your account. Contact your landlord before submitting.</p> : <><p>For {data.tenancies[0]?.propertyTitle}. Your tenancy and initial status are assigned by the server.</p><Submission navigate={navigate} onAccessLost={onAccessLost} /></>)}
    </>}
  </div>;
}
function Submission({ navigate, onAccessLost }: { navigate: (path: string) => void; onAccessLost: (error: unknown) => void }) {
  const [description, setDescription] = useState('');
  const [fieldError, setFieldError] = useState('');
  const [error, setError] = useState<unknown>();
  const [busy, setBusy] = useState(false);
  const active = useRef<AbortController | null>(null);
  const input = useRef<HTMLTextAreaElement>(null);
  useEffect(() => () => active.current?.abort(), []);
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (active.current) return;
    setError(undefined);
    const text = description.trim();
    if (text.length < 10 || text.length > 2000) { setFieldError(descriptionError); input.current?.focus(); return; }
    setFieldError(''); setBusy(true);
    const controller = new AbortController(); active.current = controller;
    try {
      const created = await apiRequest<{ id: number }>(requests, { method: 'POST', body: { description: text }, signal: controller.signal });
      if (!controller.signal.aborted) navigate(`/app/requests/${created.id}`);
    } catch (reason) {
      if (controller.signal.aborted) return;
      if (reason instanceof ApiError && [401, 403].includes(reason.status)) { onAccessLost(reason); return; }
      setError(reason);
      if (reason instanceof ApiError && reason.status === 400 && (reason.problem.errors?.description || reason.problem.errors?.Description)) { setFieldError(descriptionError); input.current?.focus(); }
    } finally { if (!controller.signal.aborted) { active.current = null; setBusy(false); } }
  }
  return <form onSubmit={event => void submit(event)} noValidate>
    {(fieldError || !!error) && <div role="alert"><p>Please review your request.</p>{fieldError && <a href="#description">{fieldError}</a>}{!!error && <p>{errorMessage(error)}</p>}</div>}
    <label htmlFor="description">Description</label><p id="description-help">Describe the issue and its location. Use 10–2000 characters after trimming. Avoid sensitive personal information.</p>
    <textarea id="description" ref={input} value={description} onChange={event => setDescription(event.target.value)} rows={7} required minLength={10} maxLength={2000} aria-invalid={!!fieldError} aria-describedby={`description-help${fieldError ? ' description-error' : ''}`} disabled={busy} />
    {fieldError && <p id="description-error">{fieldError}</p>}
    <p role="status">{busy ? 'Submitting…' : `${description.trim().length} / 2000 characters`}</p>
    <button type="submit" disabled={busy}>{busy ? 'Submitting…' : error ? 'Try submitting again' : 'Submit request'}</button> <a href="/app/requests">Cancel</a>
    {!!error && <p>A connection failure may occur after a request is saved. Check <a href="/app/requests">your requests</a> before submitting again to avoid duplicates.</p>}
  </form>;
}
