import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import App from './App';
import { ApiError } from './api/client';
import { errorMessage } from './Portal';
const home = { propertyTitle: 'My home', propertyAddress: 'Test Street', startDate: '2026-01-01' };
const item = { id: 7, propertyTitle: 'My home', propertyAddress: 'Test Street', description: '<script>private text</script>', status: 'InProgress', priority: 'High', submittedUtc: '2026-10-01T12:00:00Z', lastUpdatedUtc: '2026-10-02T12:00:00Z' };
const dashboard = { tenancies: [home], counts: { open: 2, recent: 3 }, recentMaintenanceRequests: [item] };
const detail = { request: item, history: [
  { status: 'InProgress', previousStatus: 'Submitted', occurredUtc: '2026-10-02T12:00:00Z', note: '<img src=x onerror=alert(1)>' },
  { status: 'Submitted', occurredUtc: '2026-10-01T12:00:00Z', isLegacyBaseline: true },
] };
function json(value: unknown, status = 200) { return new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } }); }
let session = { isAuthenticated: true, roles: ['Tenant'] };
let handler: (url: string, options?: RequestInit) => Response | Promise<Response>;
let fetchMock: ReturnType<typeof vi.fn>;
beforeEach(() => {
  history.replaceState(null, '', '/app/');
  session = { isAuthenticated: true, roles: ['Tenant'] };
  handler = url => url === '/api/v1/workspace' ? json({ properties: [], locationLookupEnabled: false }) : url.includes('tenant-dashboard') ? json(dashboard) : url.endsWith('/7') ? json(detail) : json({ items: [item], page: 1, pageSize: 10, totalCount: 11, totalPages: 2 });
  fetchMock = vi.fn((url: string, options?: RequestInit) => url === '/api/v1/session' ? Promise.resolve(json(session)) : url.endsWith('/antiforgery') ? Promise.resolve(json({ requestToken: 'csrf' })) : Promise.resolve(handler(url, options)));
  vi.stubGlobal('fetch', fetchMock);
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
function open(path = '/app/') { history.replaceState(null, '', path); return render(<App />); }
it('shows the overview, active home, counts and semantic navigation', async () => {
  open();
  expect(await screen.findByRole('heading', { name: 'My home' })).toBeTruthy();
  expect(screen.getByRole('region', { name: 'Maintenance summary' }).textContent).toContain('2 open requests');
  expect(screen.getByRole('navigation', { name: 'Tenant navigation' })).toBeTruthy();
  expect(screen.getByRole('main').id).toBe('main');
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'Overview' }));
});
it('shows no-tenancy and no-request states', async () => {
  handler = () => json({ tenancies: [], counts: { open: 0, recent: 0 }, recentMaintenanceRequests: [] });
  open();
  expect(await screen.findByText(/You do not have a currently active tenancy/)).toBeTruthy();
  expect(screen.getByText('No maintenance requests to show.')).toBeTruthy();
  expect(screen.getByRole('link', { name: 'Accept a valid invitation' })).toBeTruthy();
});
it('pages using server totals and clears the previous list while loading', async () => {
  handler = url => json({ items: [item], page: url.includes('page=2') ? 2 : 1, totalCount: 11, totalPages: 2, pageSize: 10 });
  open('/app/requests');
  fireEvent.click(await screen.findByRole('link', { name: 'Next page' }));
  expect(await screen.findByText('Page 2 of 2 · 11 requests')).toBeTruthy();
  expect(fetchMock).toHaveBeenCalledWith('/api/v1/maintenance-requests?page=2&pageSize=10', expect.anything());
  expect(screen.getByText('Priority: High')).toBeTruthy();
  expect(screen.queryByRole('link', { name: 'Next page' })).toBeNull();
});
it('renders ordered history, baseline, notes and UTC dates as escaped text', async () => {
  const { container } = open('/app/requests/7');
  const timeline = await screen.findByRole('list');
  const events = within(timeline).getAllByRole('listitem');
  expect(events[0]?.textContent).toContain('Legacy baseline');
  expect(events[1]?.textContent).toContain('Submitted → In progress');
  expect(events[1]?.textContent).toContain('UTC');
  expect(screen.getByText('<script>private text</script>')).toBeTruthy();
  expect(container.querySelector('script, img')).toBeNull();
});
it.each([0, 9, 2001])('rejects invalid trimmed description length %s before posting', async length => {
  open('/app/requests/new');
  const input = await screen.findByRole('textbox', { name: 'Description' });
  fireEvent.change(input, { target: { value: `  ${'x'.repeat(length)}  ` } });
  fireEvent.click(screen.getByRole('button', { name: 'Submit request' }));
  expect(screen.getByRole('alert').textContent).toContain('10 and 2000');
  expect(input.getAttribute('aria-invalid')).toBe('true');
  expect(document.activeElement).toBe(input);
  expect(fetchMock.mock.calls.some(([, o]) => o.method === 'POST')).toBe(false);
});
it.each([10, 2000])('submits boundary %s with CSRF and navigates to a fresh detail', async length => {
  handler = (url, options) => options?.method === 'POST' ? json({ id: 7 }, 201) : url.endsWith('/7') ? json(detail) : json(dashboard);
  open('/app/requests/new');
  fireEvent.change(await screen.findByRole('textbox', { name: 'Description' }), { target: { value: 'x'.repeat(length) } });
  fireEvent.click(screen.getByRole('button', { name: 'Submit request' }));
  expect(await screen.findByRole('heading', { name: 'Status timeline' })).toBeTruthy();
  const post = fetchMock.mock.calls.find(([, o]) => o.method === 'POST');
  expect(post?.[1].headers.get('X-CSRF-TOKEN')).toBe('csrf');
  expect(JSON.parse(post?.[1].body)).toEqual({ description: 'x'.repeat(length) });
  fireEvent.click(screen.getByRole('link', { name: 'Overview' }));
  await screen.findByRole('heading', { name: 'Your active tenancy' });
  expect(fetchMock.mock.calls.filter(([url]) => url === '/api/v1/tenant-dashboard')).toHaveLength(2);
});
it('maps server field validation safely and preserves the entered description', async () => {
  handler = (_url, options) => options?.method === 'POST' ? json({ code: 'validation_failed', errors: { description: ['secret exception'] }, detail: 'private trace' }, 400) : json(dashboard);
  open('/app/requests/new');
  const input = await screen.findByRole('textbox', { name: 'Description' });
  fireEvent.change(input, { target: { value: 'Kitchen tap leaking' } });
  fireEvent.click(screen.getByRole('button', { name: 'Submit request' }));
  expect(await screen.findByRole('button', { name: 'Try submitting again' })).toBeTruthy();
  expect(screen.getByRole('alert').textContent).toContain('10 and 2000');
  expect(document.body.textContent).not.toContain('secret exception');
  expect(document.body.textContent).not.toContain('private trace');
  expect((input as HTMLTextAreaElement).value).toBe('Kitchen tap leaking');
});
it.each([401, 403, 404, 409, 429, 503])('handles %s without exposing server details or stale data', async code => {
  handler = () => json({ title: 'private failure', detail: 'secret trace' }, code);
  open('/app/requests/7');
  expect(await screen.findByRole('alert')).toBeTruthy();
  expect(document.body.textContent).not.toContain('private failure');
  expect(document.body.textContent).not.toContain('secret trace');
  expect(screen.queryByText(item.description)).toBeNull();
  expect(fetchMock.mock.calls.filter(([url]) => url.endsWith('/7'))).toHaveLength(1);
});
it('retries an offline read', async () => {
  handler = () => { throw new TypeError('secret network detail'); };
  open('/app/requests/7');
  const retry = await screen.findByRole('button', { name: 'Try again' });
  handler = () => json(detail);
  fireEvent.click(retry);
  expect(await screen.findByText(item.description)).toBeTruthy();
});
it.each([{ roles: [] }, { roles: ['Landlord'] }])('clears tenant data when a session changes to $roles', async ({ roles }) => {
  open();
  await screen.findByRole('heading', { name: 'My home' });
  session = { isAuthenticated: roles.length !== 0, roles };
  fireEvent.focus(window);
  expect(screen.queryByRole('heading', { name: 'My home' })).toBeNull();
  await waitFor(() => expect(screen.queryByRole('status')).toBeNull());
  expect(screen.queryByRole('navigation', { name: 'Tenant navigation' })).toBeNull();
  expect(document.title).toBe(roles.includes('Landlord') ? 'Landlord overview · RentRight' : 'Tenant portal · RentRight');
});
it('aborts a pending read on session refresh and ignores its late result', async () => {
  let resolve!: (response: Response) => void;
  handler = () => new Promise<Response>(done => { resolve = done; });
  open();
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url.includes('tenant-dashboard'))).toBe(true));
  const signal = fetchMock.mock.calls.find(([url]) => url.includes('tenant-dashboard'))?.[1].signal;
  session = { isAuthenticated: false, roles: [] };
  fireEvent.focus(window);
  await screen.findByText('Sign in to access your RentRight account.');
  expect(signal.aborted).toBe(true);
  resolve(json(dashboard));
  await waitFor(() => expect(screen.queryByText('Test Street')).toBeNull());
});
it('maps no-active-tenancy conflict and ignores raw problem text', () => {
  expect(errorMessage(new ApiError(409, { code: 'no_active_tenancy', detail: 'private' }))).toContain('currently active tenancy');
  expect(errorMessage(new ApiError(429, { detail: 'private' }))).toContain('Please wait');
});
it('handles missing client routes without requesting tenant data', async () => {
  open('/app/unknown');
  expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeTruthy();
  expect(fetchMock).toHaveBeenCalledTimes(1);
});
it('shows loading while the overview is pending', async () => {
  handler = () => new Promise<Response>(() => {});
  open();
  expect(await screen.findByText('Loading…')).toBeTruthy();
  expect(screen.queryByText('Test Street')).toBeNull();
});
it('shows an empty maintenance list with a route back to tenancy information', async () => {
  handler = () => json({ items: [], page: 1, totalCount: 0, totalPages: 1, pageSize: 10 });
  open('/app/requests');
  expect(await screen.findByText('Page 1 of 1 · 0 requests')).toBeTruthy();
  expect(screen.getByText('No maintenance requests to show.')).toBeTruthy();
  expect(screen.getByText(/Only requests for your currently active tenancy/)).toBeTruthy();
});
it('blocks submission when no single active tenancy can be selected by the server', async () => {
  handler = () => json({ ...dashboard, tenancies: [home, home] });
  open('/app/requests/new');
  expect(await screen.findByText(/More than one active tenancy/)).toBeTruthy();
  expect(screen.queryByRole('textbox')).toBeNull();
});
it('retains form text on conflict and permits an explicit retry', async () => {
  let posts = 0;
  handler = (url, options) => options?.method === 'POST' ? ++posts === 1 ? json({ code: 'resource_conflict' }, 409) : json({ id: 7 }, 201) : url.endsWith('/7') ? json(detail) : json(dashboard);
  open('/app/requests/new');
  const input = await screen.findByRole('textbox', { name: 'Description' });
  fireEvent.change(input, { target: { value: 'Kitchen tap leaking' } });
  fireEvent.submit(input.closest('form')!);
  fireEvent.click(await screen.findByRole('button', { name: 'Try submitting again' }));
  expect(await screen.findByRole('heading', { name: 'Status timeline' })).toBeTruthy();
  expect(posts).toBe(2);
});
it('prevents duplicate pending submits and cancels the write on navigation', async () => {
  handler = (_url, options) => options?.method === 'POST' ? new Promise<Response>(() => {}) : json(dashboard);
  open('/app/requests/new');
  const input = await screen.findByRole('textbox', { name: 'Description' });
  fireEvent.change(input, { target: { value: 'Kitchen tap leaking' } });
  fireEvent.submit(input.closest('form')!);
  fireEvent.submit(input.closest('form')!);
  await waitFor(() => expect(fetchMock.mock.calls.filter(([, o]) => o.method === 'POST')).toHaveLength(1));
  expect((screen.getByRole('button', { name: 'Submitting…' }) as HTMLButtonElement).disabled).toBe(true);
  const signal = fetchMock.mock.calls.find(([, o]) => o.method === 'POST')?.[1].signal;
  fireEvent.click(screen.getByRole('link', { name: 'Overview' }));
  expect(signal.aborted).toBe(true);
});
it('clears tenant data when session revalidation fails and recovers on retry', async () => {
  open();
  await screen.findByRole('heading', { name: 'My home' });
  fetchMock.mockRejectedValueOnce(new TypeError('private network error'));
  fireEvent.focus(window);
  expect(await screen.findByRole('alert')).toBeTruthy();
  expect(screen.queryByText('Test Street')).toBeNull();
  fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
  expect(await screen.findByRole('heading', { name: 'My home' })).toBeTruthy();
});

it('keeps validation summary field anchors native without discarding form text', async () => {
  open('/app/requests/new');
  const input = await screen.findByRole('textbox', { name: 'Description' });
  fireEvent.change(input, { target: { value: 'short' } });
  fireEvent.submit(input.closest('form')!);
  const summary = screen.getByRole('alert');
  expect(fireEvent.click(within(summary).getByRole('link'))).toBe(true);
  expect(screen.getByRole('textbox', { name: 'Description' })).toBe(input);
  expect((input as HTMLTextAreaElement).value).toBe('short');
});

it('shows the explicitly labelled demo without calling the session or tenant APIs', async () => {
  open('/app/demo/');
  expect(await screen.findByRole('heading', { name: 'The Willow, Apartment 4B' })).toBeTruthy();
  expect(screen.getByText('Portfolio demo')).toBeTruthy();
  expect(screen.getByText(/fictional data/)).toBeTruthy();
  expect(screen.getByRole('region', { name: 'Maintenance summary' }).textContent).toContain('3 open requests');
  expect(fetchMock).not.toHaveBeenCalled();
});

it('simulates a demo submission and refreshes its overview without any network write', async () => {
  open('/app/demo/requests/new');
  const input = await screen.findByRole('textbox', { name: 'Description' });
  fireEvent.change(input, { target: { value: '<script>A fictional loose door handle</script>' } });
  fireEvent.submit(input.closest('form')!);
  expect(await screen.findByRole('heading', { name: 'Request #1049' })).toBeTruthy();
  expect(await screen.findByRole('heading', { name: '<script>A fictional loose door handle</script>' })).toBeTruthy();
  expect(document.querySelector('script')).toBeNull();
  fireEvent.click(screen.getByRole('link', { name: 'Overview' }));
  await screen.findByRole('heading', { name: 'Your active tenancy' });
  expect(screen.getByRole('region', { name: 'Maintenance summary' }).textContent).toContain('4 open requests');
  expect(fetchMock).not.toHaveBeenCalled();
});

it('never treats a similar route name as a demo or bypasses a live session', async () => {
  session = { isAuthenticated: false, roles: [] };
  open('/app/demonstration');
  expect(await screen.findByText('Sign in to access your RentRight account.')).toBeTruthy();
  expect(fetchMock).toHaveBeenCalledWith('/api/v1/session', expect.anything());
  expect(screen.queryByText('Portfolio demo')).toBeNull();
});

it('requires the real session when leaving the demo', async () => {
  session = { isAuthenticated: false, roles: [] };
  open('/app/demo/');
  await screen.findByRole('heading', { name: 'The Willow, Apartment 4B' });
  history.pushState(null, '', '/app/');
  fireEvent.popState(window);
  expect(await screen.findByText('Sign in to access your RentRight account.')).toBeTruthy();
  expect(screen.queryByText('24 Willow Lane · Demo address')).toBeNull();
  expect(fetchMock).toHaveBeenCalledTimes(1);
});

it('opens mobile navigation, supports Escape, and restores focus', async () => {
  open('/app/demo/');
  await screen.findByRole('heading', { name: 'Overview' });
  const toggle = screen.getByRole('button', { name: 'Open navigation' });
  fireEvent.click(toggle);
  const dialog = screen.getByRole('dialog', { name: 'Workspace navigation' });
  expect(dialog.getAttribute('aria-modal')).toBe('true');
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Close navigation' }));
  fireEvent.keyDown(dialog, { key: 'Escape' });
  expect(screen.queryByRole('dialog')).toBeNull();
  expect(document.activeElement).toBe(toggle);
});

it('releases the inert background when a drawer is resized to desktop', async () => {
  open('/app/demo/');
  await screen.findByRole('heading', { name: 'Overview' });
  fireEvent.click(screen.getByRole('button', { name: 'Open navigation' }));
  expect(screen.getByRole('dialog')).toBeTruthy();
  vi.stubGlobal('innerWidth', 1024);
  fireEvent(window, new Event('resize'));
  expect(screen.queryByRole('dialog')).toBeNull();
  expect(screen.getByRole('main').parentElement?.hasAttribute('inert')).toBe(false);
});
