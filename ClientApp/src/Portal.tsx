import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { ApiError, apiRequest } from './api/client';
import type { ApiPage } from './api/client';
import type { Dashboard, Detail, MaintenanceItem, RecentRequest } from './api/tenant';
import { BuildingIllustration, Icon } from './components/Icon';

const requests = '/api/v1/maintenance-requests';
const descriptionError = 'Enter a description between 10 and 2000 characters after trimming.';

export function errorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) return 'We couldn’t connect. Check your connection and try again.';
  switch (error.status) {
    case 400: return 'Check the form and try again.';
    case 401: return 'Your session has ended. Sign in again.';
    case 403: return 'You don’t have access to this resource.';
    case 404: return 'This request is unavailable or no longer accessible.';
    case 409: return error.problem.code === 'no_active_tenancy' ? 'You need a currently active tenancy to submit a request.' : 'Your tenancy or request has changed. Review the overview before trying again.';
    case 429: return 'Too many requests. Please wait before trying again.';
    case 503: return 'The service is temporarily unavailable. Please try again later.';
    default: return 'We couldn’t complete the request. Please try again.';
  }
}

export function Failure({ error, retry }: { error: unknown; retry: () => void }) {
  return <div className="error-state" role="alert"><span className="state-icon"><Icon name="help" /></span><h2>Let’s try that again.</h2><p>{errorMessage(error)}</p>{error instanceof ApiError && error.status === 401
    ? <a className="button button-primary" href="/Identity/Account/Login?returnUrl=%2Fapp%2F">Sign in</a> : <button className="button button-primary" onClick={retry}>Try again</button>}</div>;
}

function utc(value: string) { return new Date(value).toLocaleString('en-IE', { timeZone: 'UTC', dateStyle: 'medium', timeStyle: 'short' }) + ' UTC'; }
function date(value: string) { return new Date(value).toLocaleDateString('en-IE', { timeZone: 'UTC', day: 'numeric', month: 'short', year: 'numeric' }); }
function status(value: string) { return value === 'InProgress' ? 'In progress' : value; }
function StatusBadge({ value }: { value: string }) {
  const tone = value === 'InProgress' ? 'progress' : value === 'Submitted' ? 'submitted' : ['Resolved', 'Closed'].includes(value) ? 'complete' : 'neutral';
  return <span className={`status-badge status-${tone}`}><span />{status(value)}</span>;
}
function NoTenancy() { return <div className="empty-state"><Icon name="home" /><h3>A home is waiting for you.</h3><p>You do not have a currently active tenancy. <a href="/app/tenancies/accept">Accept a valid invitation</a> or contact your landlord.</p></div>; }

function RequestList({ items, basePath, compact = false }: { items: RecentRequest[]; basePath: string; compact?: boolean }) {
  return items.length === 0 ? <div className="empty-state"><Icon name="requests" /><p>No maintenance requests to show.</p><span>When you report an issue, you can follow its progress here.</span></div> : <ul className={`requests ${compact ? 'requests-compact' : ''}`}>{items.map(item => {
    const detailed = 'description' in item ? item as MaintenanceItem : undefined;
    return <li key={item.id}>
      <span className={`request-symbol ${item.status === 'Resolved' || item.status === 'Closed' ? 'symbol-complete' : ''}`}><Icon name={item.status === 'Resolved' || item.status === 'Closed' ? 'check' : 'tool'} /></span>
      <div className="request-copy"><span className="request-reference">{detailed ? `REQUEST #${item.id}` : item.propertyTitle}</span><a href={`${basePath}/requests/${item.id}`}>{detailed?.description ?? `Maintenance request #${item.id}`}</a><span className="request-date"><time dateTime={item.submittedUtc}>{date(item.submittedUtc)}</time>{detailed && <span className="priority-label">Priority: {detailed.priority}</span>}</span></div>
      <StatusBadge value={item.status} /><a className="request-arrow" href={`${basePath}/requests/${item.id}`} aria-label={`View request ${item.id}`}><Icon name="chevron" /></a>
    </li>;
  })}</ul>;
}

function Overview({ data, basePath }: { data: Dashboard; basePath: string }) {
  return <>
    <div className="summary-grid">
      <section className="summary-card"><span className="summary-icon"><Icon name="home" /></span><span className="summary-label">Your space</span><strong>{data.tenancies.length === 1 ? 'Home, sorted.' : `${data.tenancies.length} active tenancies`}</strong><span className="summary-foot">{data.tenancies.length ? 'Your rental details, all together' : 'Your next chapter starts here'}</span></section>
      <section className="summary-card" aria-label="Maintenance summary"><span className="summary-icon summary-icon-amber"><Icon name="tool" /></span><span className="summary-label">Maintenance</span><strong>{data.counts.open}<span className="summary-unit"> open requests</span></strong><span className="summary-foot">Submitted or being worked on</span></section>
      <section className="summary-card"><span className="summary-icon summary-icon-blue"><Icon name="clock" /></span><span className="summary-label">Last 30 days</span><strong>{data.counts.recent}<span className="summary-unit"> recent requests</span></strong><span className="summary-foot">Submitted in the last 30 days</span></section>
    </div>
    <div className="overview-grid"><section className="panel home-panel"><div className="section-heading"><div><p className="eyebrow">YOUR PLACE</p><h2>Your active tenancy</h2></div><span className="tiny-icon"><Icon name="home" /></span></div>
      {data.tenancies.length === 0 ? <NoTenancy /> : data.tenancies.map((home, i) => <div className="home-card" key={i}><div className="home-art"><span className="home-art-label"><span />Currently active</span><BuildingIllustration /><span className="art-caption">A place to put down roots.</span></div><div className="home-information"><span className="eyebrow">YOUR HOME</span><h3>{home.propertyTitle}</h3><p className="address"><Icon name="pin" />{home.propertyAddress}</p><div className="tenancy-dates"><Icon name="calendar" /><div><span>Tenancy period</span><strong><time dateTime={home.startDate}>{date(home.startDate)}</time><span className="date-separator">—</span>{home.endDate ? <time dateTime={home.endDate}>{date(home.endDate)}</time> : 'Open-ended'}</strong></div></div></div></div>)}
    </section>
    <div className="overview-right"><section className="panel recent-panel"><div className="section-heading"><div><p className="eyebrow">KEEPING THINGS MOVING</p><h2>Recent requests</h2></div><a className="text-link" href={`${basePath}/requests`}>View all <Icon name="arrow" /></a></div><RequestList items={data.recentMaintenanceRequests} basePath={basePath} compact /></section>
      <section className="report-card"><span className="report-icon"><Icon name="tool" /></span><div><h2>Small issue? Let’s sort it.</h2><p>Tell us what needs attention. Follow every step, from reported to resolved.</p><a className="text-link" href={`${basePath}/requests/new`}>Report an issue <Icon name="arrow" /></a></div><span className="report-decoration" aria-hidden="true">✳</span></section>
    </div></div>
    <div className="quiet-note"><Icon name="shield" /><p>Your rental information stays private. Only you and the people managing your home can access your requests.</p></div>
  </>;
}

function RequestDetail({ data, basePath }: { data: Detail; basePath: string }) {
  return <><a className="back-link" href={`${basePath}/requests`}>← All maintenance requests</a><div className="detail-grid">
    <section className="panel detail-panel"><div className="section-heading"><span className="eyebrow">REQUEST DETAILS</span><StatusBadge value={data.request.status} /></div><span className="detail-tool"><Icon name="tool" /></span><h2>{data.request.description}</h2><p className="address"><Icon name="pin" />{data.request.propertyTitle}</p><p className="detail-address">{data.request.propertyAddress}</p><dl className="request-facts"><div><dt>Priority</dt><dd><span className={`priority-dot ${data.request.priority === 'High' || data.request.priority === 'Urgent' ? 'priority-high' : ''}`} />{data.request.priority}</dd></div><div><dt>Submitted</dt><dd><time dateTime={data.request.submittedUtc}>{utc(data.request.submittedUtc)}</time></dd></div><div><dt>Last updated</dt><dd><time dateTime={data.request.lastUpdatedUtc}>{utc(data.request.lastUpdatedUtc)}</time></dd></div></dl><div className="detail-reassurance"><Icon name="shield" /><p>You’re in the loop. Updates from your property team will appear in the timeline.</p></div></section>
    <section className="panel timeline-panel"><div className="section-heading"><div><p className="eyebrow">EVERY STEP, IN ONE PLACE</p><h2>Status timeline</h2></div><Icon name="clock" /></div>{data.history.length === 0 ? <p className="panel-padding">No status history is available.</p> : <ol className="timeline">{[...data.history].sort((a, b) => Date.parse(a.occurredUtc) - Date.parse(b.occurredUtc)).map((event, i) => <li key={i}><span className="timeline-marker"><Icon name={event.status === 'Submitted' ? 'requests' : event.status === 'InProgress' ? 'tool' : 'check'} /></span><div><span className="timeline-label">{event.previousStatus ? `${status(event.previousStatus)} → ` : ''}{status(event.status)}</span><time dateTime={event.occurredUtc}>{utc(event.occurredUtc)}</time>{event.isLegacyBaseline && <p className="timeline-note">Legacy baseline: the state observed when history recording began, not a recorded transition.</p>}{event.note && <p className="timeline-note">{event.note.slice(0, 1000)}</p>}</div></li>)}</ol>}<div className="timeline-footer"><span className="live-dot" />Updated when your property team makes a change</div></section>
  </div></>;
}

export function Portal({ route, navigate, onAccessLost, request = apiRequest, basePath = '/app', demo = false }: {
  route: string; navigate: (path: string) => void; onAccessLost: (error: unknown) => void; request?: typeof apiRequest; basePath?: string; demo?: boolean;
}) {
  const url = new URL(route, location.origin);
  const path = url.pathname.replace(/\/$/, '');
  const isOverview = path === basePath;
  const isList = path === `${basePath}/requests`;
  const isNew = path === `${basePath}/requests/new`;
  const relative = path.startsWith(basePath + '/') ? path.slice(basePath.length) : '';
  const id = /^\/requests\/([1-9]\d*)$/.exec(relative)?.[1];
  const parsedPage = Number(url.searchParams.get('page') ?? 1);
  const page = Number.isInteger(parsedPage) && parsedPage >= 1 && parsedPage <= 10000 ? parsedPage : 1;
  const title = isOverview ? 'Overview' : isList ? 'Maintenance requests' : isNew ? 'New maintenance request' : id ? `Request #${id}` : 'Page not found';
  const [data, setData] = useState<Dashboard | ApiPage<MaintenanceItem> | Detail>();
  const [error, setError] = useState<unknown>();
  const [attempt, setAttempt] = useState(0);
  const heading = useRef<HTMLHeadingElement>(null);
  const endpoint = isOverview || isNew ? '/api/v1/tenant-dashboard' : isList ? `${requests}?page=${page}&pageSize=10` : id ? `${requests}/${id}` : undefined;
  useEffect(() => { document.title = `${title} · RentRight${demo ? ' demo' : ''}`; heading.current?.focus(); }, [title, demo]);
  useEffect(() => {
    const controller = new AbortController();
    if (endpoint) void request<Dashboard | ApiPage<MaintenanceItem> | Detail>(endpoint, { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) setData(value); })
      .catch(reason => {
        if (controller.signal.aborted) return;
        if (reason instanceof ApiError && [401, 403].includes(reason.status)) onAccessLost(reason);
        setError(reason);
      });
    return () => controller.abort();
  }, [endpoint, attempt, onAccessLost, request]);
  const retry = () => { setData(undefined); setError(undefined); setAttempt(v => v + 1); };
  return <>
    <div className="page-heading"><div><p className="eyebrow">{isOverview ? 'A LITTLE MORE AT HOME' : 'YOUR HOME, TAKEN CARE OF'}</p><h1 tabIndex={-1} ref={heading}>{title}</h1><p className="page-description">{isOverview ? 'Your home, your requests. Everything in one place.' : isList ? 'From the first report to the final fix. Follow it all here.' : isNew ? 'Tell us what needs attention. We’ll take it from here.' : id ? 'A clear view of what’s happening and what comes next.' : 'Let’s get you back to familiar ground.'}</p></div>{!isNew && !id && endpoint && <a className="button button-primary" aria-label="Report an issue" href={`${basePath}/requests/new`}><Icon name="plus" />Report an issue</a>}</div>
    {!endpoint ? <div className="empty-state"><Icon name="home" /><p>Choose a page from the tenant navigation.</p><a className="button button-primary" href={`${basePath}/`}>Back to overview</a></div> : error ? <Failure error={error} retry={retry} /> : !data ? <div className="loading-state" role="status"><span className="loading-ring" />Loading…</div> : <>
      {isOverview && 'tenancies' in data && <Overview data={data} basePath={basePath} />}
      {isList && 'items' in data && <section className="panel list-panel"><div className="section-heading"><div><p className="eyebrow">MAINTENANCE LOG</p><h2>All requests <span className="count-chip">{data.totalCount}</span></h2></div><span className="muted">Most recent first</span></div><RequestList items={data.items} basePath={basePath} /><div className="list-pagination"><p role="status">Page {data.page} of {data.totalPages} · {data.totalCount} requests</p><nav aria-label="Request pages">{data.page > 1 && <a className="button button-secondary" href={`${basePath}/requests?page=${data.page - 1}`}>Previous page</a>}{data.page < data.totalPages && <a className="button button-secondary" href={`${basePath}/requests?page=${data.page + 1}`}>Next page <Icon name="arrow" /></a>}</nav></div>{data.totalCount === 0 && <p className="empty-list-note">Only requests for your currently active tenancy appear here. Check <a href={`${basePath}/`}>Overview</a> for your tenancy.</p>}</section>}
      {id && 'request' in data && <RequestDetail data={data} basePath={basePath} />}
      {isNew && 'tenancies' in data && (data.tenancies.length === 0 ? <NoTenancy /> : data.tenancies.length > 1 ? <div className="empty-state"><p>More than one active tenancy is associated with your account. Contact your landlord before submitting.</p></div> : <div className="submission-grid"><section className="panel submission-panel"><div className="section-heading"><div><p className="eyebrow">LET’S GET IT SORTED</p><h2>What’s the issue?</h2></div><Icon name="tool" /></div><div className="submission-body"><p className="submission-property"><Icon name="home" />{data.tenancies[0]?.propertyTitle}</p><Submission navigate={navigate} onAccessLost={onAccessLost} request={request} basePath={basePath} demo={demo} /></div></section><aside className="submission-guide"><span className="guide-icon"><Icon name="help" /></span><h2>A little detail goes a long way.</h2><p>A clear description helps your property team get the right fix, faster.</p><ul><li>Where in your home is the issue?</li><li>When did you first notice it?</li><li>What happens when you use it?</li></ul><div className="guide-emergency"><strong>Is it an emergency?</strong><p>For an immediate danger, contact the appropriate emergency service. This form is for routine maintenance.</p></div>{demo && <p className="demo-disclosure">Demo submissions are simulated, stay in this page session, and disappear on reload. Please use fictional information only.</p>}</aside></div>)}
    </>}
  </>;
}

function Submission({ navigate, onAccessLost, request, basePath, demo }: {
  navigate: (path: string) => void; onAccessLost: (error: unknown) => void; request: typeof apiRequest; basePath: string; demo: boolean;
}) {
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
      const created = await request<{ id: number }>(requests, { method: 'POST', body: { description: text }, signal: controller.signal });
      if (!controller.signal.aborted) navigate(`${basePath}/requests/${created.id}`);
    } catch (reason) {
      if (controller.signal.aborted) return;
      if (reason instanceof ApiError && [401, 403].includes(reason.status)) { onAccessLost(reason); return; }
      setError(reason);
      if (reason instanceof ApiError && reason.status === 400 && (reason.problem.errors?.description || reason.problem.errors?.Description)) { setFieldError(descriptionError); input.current?.focus(); }
    } finally { if (!controller.signal.aborted) { active.current = null; setBusy(false); } }
  }
  return <form onSubmit={event => void submit(event)} noValidate>
    {(fieldError || !!error) && <div className="validation-summary" role="alert"><p>Please review your request.</p>{fieldError && <a href="#description">{fieldError}</a>}{!!error && <p>{errorMessage(error)}</p>}</div>}
    <label htmlFor="description">Description <span aria-hidden="true">*</span></label><p id="description-help" className="field-help">Describe the issue and its location. Use 10–2000 characters after trimming. Avoid sensitive personal information.</p>
    <textarea id="description" ref={input} value={description} onChange={event => setDescription(event.target.value)} placeholder="For example, the kitchen tap has been dripping since yesterday, even when it’s fully turned off…" rows={7} required minLength={10} maxLength={2000} aria-invalid={!!fieldError} aria-describedby={`description-help${fieldError ? ' description-error' : ''}`} disabled={busy} />
    {fieldError && <p id="description-error" className="field-error">{fieldError}</p>}
    <p className="character-count" role="status">{busy ? 'Submitting…' : `${description.trim().length} / 2000 characters`}</p>
    <div className="form-actions"><a className="button button-secondary" href={`${basePath}/requests`}>Cancel</a><button className="button button-primary" type="submit" disabled={busy}>{busy ? 'Submitting…' : error ? 'Try submitting again' : 'Submit request'}<Icon name="arrow" /></button></div>
    <p className="form-reassurance"><Icon name="shield" />{demo ? 'Demo only. No request will be sent to a property team.' : 'Your request goes directly to the team managing your home.'}</p>
    {!!error && <p>A connection failure may occur after a request is saved. Check <a href={`${basePath}/requests`}>your requests</a> before submitting again to avoid duplicates.</p>}
  </form>;
}
