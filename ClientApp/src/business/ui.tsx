import { useEffect, useRef, useState } from 'react';
import type { FormEvent, ReactNode } from 'react';
import { ApiError, apiRequest } from '../api/client';
import type { ApiPage, RequestOptions } from '../api/client';
import { Failure, errorMessage } from '../Portal';

export type AccessLost = (reason: unknown) => void;
export type Navigate = (path: string) => void;
export interface PageProps { route: string; navigate: Navigate; onAccessLost: AccessLost }
export const money = (value: number) => new Intl.NumberFormat('en-IE', { style: 'currency', currency: 'EUR' }).format(value);
export const label = (value: string) => value === 'InProgress' ? 'In progress' : value;
export function pageNumber(route: string) {
  const value = Number(new URL(route, location.origin).searchParams.get('page') ?? 1);
  return Number.isInteger(value) && value > 0 && value <= 10000 ? value : 1;
}
export function PageHeading({ title, children }: { title: string; children?: ReactNode }) {
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { document.title = `${title} · RentRight`; heading.current?.focus(); }, [title]);
  return <div className="page-heading"><div><p className="eyebrow">YOUR PROPERTY WORKSPACE</p><h1 ref={heading} tabIndex={-1}>{title}</h1></div><div className="business-actions">{children}</div></div>;
}
export function Remote<T>({ path, onAccessLost, children }: { path: string; onAccessLost: AccessLost; children: (data: T, reload: () => void) => ReactNode }) {
  const [result, setResult] = useState<{ data?: T; error?: unknown }>({});
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    void apiRequest<T>(path, { signal: controller.signal }).then(data => {
      if (!controller.signal.aborted) setResult({ data });
    }).catch(error => {
      if (controller.signal.aborted) return;
      if (error instanceof ApiError && [401, 403].includes(error.status)) onAccessLost(error);
      setResult({ error });
    });
    return () => controller.abort();
  }, [path, attempt, onAccessLost]);
  const reload = () => { setResult({}); setAttempt(value => value + 1); };
  return result.error ? <Failure error={result.error} retry={reload} /> : result.data === undefined
    ? <p role="status">Loading…</p> : children(result.data, reload);
}
export function MutationError({ error }: { error: unknown }) {
  if (!error) return null;
  const code = error instanceof ApiError ? error.problem.code : undefined;
  return <div className="validation-summary" role="alert">
    <p>{code === 'last_administrator' ? 'At least one usable administrator must remain.' : code === 'reauthentication_required'
      ? 'Verify your password before changing roles. Nothing has been changed.' : error instanceof ApiError && error.status === 409 ? 'The record changed or this operation is blocked.' : errorMessage(error)}</p>
    {error instanceof ApiError && error.status === 400 && <p>Review the labelled fields, required values, dates and lengths. Your entered values are retained.</p>}
    {code === 'reauthentication_required' && <a href="/Identity/Account/Reauthenticate?returnUrl=%2Fapp%2Fadmin%2Fusers">Verify password, then return and submit again</a>}
    {error instanceof ApiError && error.status === 409 && <p>Reload the latest record before trying again.</p>}
    {!(error instanceof ApiError) && <p>The change may already have been saved. Check the latest record before submitting again.</p>}
  </div>;
}
// One explicit write, never an automatic retry. Abort on navigation and ignore late results.
export function ActionForm({ children, submit, onSuccess, onAccessLost, submitLabel = 'Save changes', submitAriaLabel, danger = false }: {
  children?: ReactNode; submit: (data: FormData, signal: AbortSignal) => Promise<unknown>;
  onSuccess: (value: unknown) => void; onAccessLost: AccessLost; submitLabel?: string; submitAriaLabel?: string; danger?: boolean;
}) {
  const active = useRef<AbortController | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  useEffect(() => () => active.current?.abort(), []);
  async function send(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (active.current) return;
    const data = new FormData(event.currentTarget);
    const controller = new AbortController(); active.current = controller;
    setBusy(true); setError(undefined);
    try {
      const value = await submit(data, controller.signal);
      if (!controller.signal.aborted) onSuccess(value);
    } catch (reason) {
      if (controller.signal.aborted) return;
      if (reason instanceof ApiError && [401, 403].includes(reason.status) && reason.problem.code !== 'reauthentication_required') onAccessLost(reason);
      setError(reason);
    } finally { if (!controller.signal.aborted) { active.current = null; setBusy(false); } }
  }
  return <form onSubmit={event => void send(event)} className="business-form"><MutationError error={error} /><fieldset disabled={busy}>
    {children}<button className={`button ${danger ? 'button-danger' : 'button-primary'}`} type="submit" aria-label={submitAriaLabel} disabled={busy}>{busy ? 'Saving…' : submitLabel}</button>
  </fieldset></form>;
}
export function write(path: string, options: RequestOptions, signal: AbortSignal) { return apiRequest(path, { ...options, signal }); }
export function Paging({ data, route }: { data: Pick<ApiPage<unknown>, 'page' | 'totalPages' | 'totalCount'>; route: string }) {
  const url = new URL(route, location.origin);
  const pageLink = (page: number) => { const query = new URLSearchParams(url.search); query.set('page', String(page)); return url.pathname + '?' + query; };
  return <div className="list-pagination"><p role="status">Page {data.page} of {data.totalPages} · {data.totalCount} results</p><nav aria-label="Result pages">
    {data.page > 1 && <a className="button button-secondary" href={pageLink(data.page - 1)}>Previous page</a>}
    {data.page < data.totalPages && <a className="button button-secondary" href={pageLink(data.page + 1)}>Next page</a>}
  </nav></div>;
}
export interface Property { id: number; title: string; address: string; rentAmount: number; description?: string | null }
export interface ManagedProperty extends Property {
  version: string; canManage: boolean; locationLookupEnabled: boolean;
  location?: { latitude: number; longitude: number; geocodedUtc: string } | null;
}
export interface WorkspaceSummary { totalUsers?: number; totalProperties?: number; properties: Property[]; locationLookupEnabled: boolean }
export function LocationCard({ location }: { location?: ManagedProperty['location'] }) {
  if (!location) return null;
  return <section className="business-card"><h2>Approximate location</h2><p>{location.latitude.toFixed(6)}, {location.longitude.toFixed(6)} — an estimated address match, not a verified boundary.</p>
    <a className="text-link" target="_blank" rel="noopener noreferrer" href={`https://www.openstreetmap.org/?mlat=${location.latitude}&mlon=${location.longitude}#map=16/${location.latitude}/${location.longitude}`}>Open location on OpenStreetMap (new tab)</a>
    <p className="field-help">Updated {new Date(location.geocodedUtc.endsWith('Z') ? location.geocodedUtc : location.geocodedUtc + 'Z').toLocaleString('en-IE', { timeZone: 'UTC' })} UTC. Powered by <a href="https://www.geoapify.com/">Geoapify</a> · <a href="https://www.openstreetmap.org/copyright">© OpenStreetMap contributors</a>.</p>
  </section>;
}
