import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import App from '../App';
const property = { id: 7, title: 'Willow House', address: 'Synthetic Street', rentAmount: 1250, description: '<script>Plain description</script>', version: 'AAAAAAAAAAE=', canManage: true, locationLookupEnabled: false };
const summary = { totalUsers: 6, totalProperties: 2, properties: [property], locationLookupEnabled: false };
const tenancy = { id: 3, propertyTitle: 'Willow House', propertyAddress: 'Synthetic Street', tenantName: 'Fictional tenant', tenantEmail: 'resident@example.test', startDate: '2026-01-01', status: 'Active', version: 'AAAAAAAAAAI=' };
const triage = { request: { id: 8, propertyId: 7, propertyTitle: 'Willow House', status: 'Submitted', priority: 'Normal', submittedUtc: '2026-10-01T12:00:00Z', revision: 4 }, description: 'Fictional kitchen tap leak', allowedStatuses: ['InProgress'], history: [{ status: 'Submitted', occurredUtc: '2026-10-01T12:00:00Z', isLegacyBaseline: false }] };
const users = { users: [{ userId: 'target', fullName: 'Fictional account', email: 'account@example.test', roles: ['Tenant'] }], availableRoles: ['Admin', 'Landlord', 'Tenant'], page: 1, pageSize: 25, totalUsers: 1, totalPages: 1 };
const page = (items: unknown[]) => ({ items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 });
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
let roles: string[];
let signedIn: boolean;
let handle: (url: string, options: RequestInit) => Response | Promise<Response>;
let fetchMock: ReturnType<typeof vi.fn>;
beforeEach(() => {
  roles = ['Landlord']; signedIn = true;
  handle = (url, options) => {
    if (options.method !== 'GET') return new Response(null, { status: 204 });
    if (url === '/api/v1/workspace') return json(summary);
    if (url.includes('invitation-form')) return json({ defaultStartDate: '2026-10-18', availableProperties: [{ value: '7', label: 'Willow House' }] });
    if (url.includes('/workspace/tenancies?')) return json(page([tenancy]));
    if (url === '/api/v1/tenancies/3') return json(tenancy);
    if (url.startsWith('/api/v1/admin-users')) return json(users);
    if (url === '/api/v1/landlord-maintenance-requests/8') return json(triage);
    if (url.includes('landlord-maintenance-requests?')) return json(page([triage.request]));
    if (url.includes('/properties/7') || url.includes('/published-properties/7')) return json(property);
    return json(page([property]));
  };
  fetchMock = vi.fn((url: string, options: RequestInit) => Promise.resolve(url === '/api/v1/session' ? json({ isAuthenticated: signedIn, roles })
    : url === '/api/v1/session/antiforgery' ? json({ requestToken: 'test-csrf' }) : handle(url, options)));
  vi.stubGlobal('fetch', fetchMock);
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
function open(path: string) { history.replaceState(null, '', path); return render(<App />); }
function post(method: string) { return fetchMock.mock.calls.find(([, options]) => options.method === method); }
function change(name: string, value: string) { fireEvent.change(screen.getByLabelText(name), { target: { value } }); }
it('opens an owning landlord workspace instead of rejecting a non-tenant', async () => {
  open('/app/');
  expect(await screen.findByRole('heading', { name: 'Landlord overview' })).toBeTruthy();
  expect(await screen.findByRole('heading', { name: 'Willow House' })).toBeTruthy();
  expect(screen.getByRole('navigation', { name: 'Management navigation' })).toBeTruthy();
  expect(screen.queryByRole('navigation', { name: 'Tenant navigation' })).toBeNull();
  expect(fetchMock.mock.calls.some(([url]) => url.includes('tenant-dashboard'))).toBe(false);
});
it('keeps tenant and management navigation available for a multi-role administrator', async () => {
  roles = ['Admin', 'Landlord', 'Tenant']; open('/app/manage');
  expect(await screen.findByRole('heading', { name: 'Admin overview' })).toBeTruthy();
  expect(await screen.findByText('Registered users')).toBeTruthy();
  expect(screen.getByRole('link', { name: 'User roles' })).toBeTruthy();
  expect(screen.getByRole('navigation', { name: 'Tenant navigation' })).toBeTruthy();
});
it.each(['/app/manage', '/app/admin/users'])('denies tenant-only access to %s without private reads', async route => {
  roles = ['Tenant']; open(route);
  expect(await screen.findByRole('heading', { name: 'Manager access required' })).toBeTruthy();
  expect(fetchMock).toHaveBeenCalledTimes(1);
});
it('does not expose the admin users endpoint to a landlord', async () => {
  open('/app/admin/users'); await screen.findByRole('heading', { name: 'Page unavailable' });
  expect(fetchMock).toHaveBeenCalledTimes(1);
});
it('allows anonymous property browsing and detail while escaping supplied content', async () => {
  signedIn = false; roles = []; open('/app/properties/7');
  expect(await screen.findByRole('heading', { name: 'Willow House' })).toBeTruthy();
  expect(screen.getByText(property.description)).toBeTruthy();
  expect(document.querySelector('script')).toBeNull();
  expect(screen.queryByRole('link', { name: /Manage this property/ })).toBeNull();
});
it('searches public properties with encoded filters and deterministic paging', async () => {
  signedIn = false; roles = []; open('/app/properties');
  await screen.findByRole('heading', { name: 'Willow House' });
  change('Search title or address', 'Willow & Lane'); change('Minimum monthly rent', '1000');
  fireEvent.click(screen.getByRole('button', { name: 'Search properties' }));
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url.includes('search=Willow+%26+Lane') && url.includes('minimumRent=1000'))).toBe(true));
});
it('does not show management controls for a property owned by another landlord', async () => {
  handle = () => json({ ...property, canManage: false });
  open('/app/properties/7'); await screen.findByRole('heading', { name: 'Willow House' });
  expect(screen.queryByRole('link', { name: 'Manage this property' })).toBeNull();
});
it('creates a property with only allow-listed fields and a fresh CSRF token', async () => {
  handle = (url, options) => options.method === 'POST' ? json(property, 201) : url === '/api/v1/workspace' ? json(summary) : json(property);
  open('/app/manage/properties/new'); await screen.findByLabelText('Title');
  change('Title', 'New home'); change('Address', 'Fictional address'); change('Monthly rent (€)', '1400');
  fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
  await screen.findByRole('heading', { name: 'Manage property' });
  const call = post('POST')!;
  expect(call[1].headers.get('X-CSRF-TOKEN')).toBe('test-csrf');
  expect(JSON.parse(call[1].body)).toEqual({ title: 'New home', address: 'Fictional address', rentAmount: 1400, description: null });
  expect(location.pathname).toBe('/app/manage/properties/7');
});
it('edits a property using its observed version', async () => {
  open('/app/manage/properties/7/edit'); await screen.findByLabelText('Title'); change('Title', 'Renamed home');
  fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
  await waitFor(() => expect(post('PUT')).toBeTruthy());
  expect(JSON.parse(post('PUT')![1].body).expectedVersion).toBe(property.version);
});
it('preserves an untouched stored address exactly during a rent-only edit', async () => {
  const originalAddress = '  Legacy address with spaces  ';
  handle = (_url, options) => options.method === 'PUT' ? new Response(null, { status: 204 }) : json({ ...property, address: originalAddress });
  open('/app/manage/properties/7/edit'); await screen.findByLabelText('Address'); change('Monthly rent (€)', '1500');
  fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
  await waitFor(() => expect(post('PUT')).toBeTruthy());
  expect(JSON.parse(post('PUT')![1].body).address).toBe(originalAddress);
});
it('keeps entered values and does not automatically retry a conflicting property edit', async () => {
  handle = (_url, options) => options.method === 'PUT' ? json({ detail: 'private trace', code: 'resource_conflict' }, 409) : json(property);
  open('/app/manage/properties/7/edit'); await screen.findByLabelText('Title'); change('Title', 'My unsaved title');
  fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));
  expect(await screen.findByRole('alert')).toBeTruthy();
  expect((screen.getByLabelText('Title') as HTMLInputElement).value).toBe('My unsaved title');
  expect(fetchMock.mock.calls.filter(([, options]) => options.method === 'PUT')).toHaveLength(1);
  expect(document.body.textContent).not.toContain('private trace');
});
it('requires explicit property deletion and sends the expected-version header', async () => {
  open('/app/manage/properties/7/delete'); await screen.findByRole('heading', { name: 'Delete Willow House?' });
  fireEvent.click(screen.getByRole('checkbox')); fireEvent.click(screen.getByRole('button', { name: 'Confirm property deletion' }));
  await waitFor(() => expect(post('DELETE')).toBeTruthy());
  expect(post('DELETE')![1].headers.get('X-Expected-Version')).toBe(property.version);
});
it('renders an attributed stored location without requesting third-party tiles', async () => {
  handle = () => json({ ...property, location: { latitude: 53.3, longitude: -6.2, geocodedUtc: '2026-10-18T12:00:00Z' } });
  open('/app/properties/7'); await screen.findByRole('heading', { name: 'Approximate location' });
  const link = screen.getByRole('link', { name: 'Open location on OpenStreetMap (new tab)' });
  expect(link.getAttribute('rel')).toBe('noopener noreferrer');
  expect(fetchMock.mock.calls.every(([url]) => url.startsWith('/api/v1/'))).toBe(true);
});
it('lists manager-scoped tenancy names and sends a versioned end operation', async () => {
  open('/app/manage/tenancies'); await screen.findByText('Fictional tenant');
  fireEvent.click(screen.getByRole('link', { name: 'End tenancy' }));
  await screen.findByRole('heading', { name: 'End or cancel tenancy' }); await screen.findByRole('checkbox');
  fireEvent.click(screen.getByRole('checkbox')); fireEvent.click(screen.getByRole('button', { name: 'Confirm tenancy end' }));
  await waitFor(() => expect(post('POST')).toBeTruthy());
  expect(post('POST')![0]).toBe('/api/v1/tenancies/3/end');
  expect(post('POST')![1].headers.get('X-Expected-Version')).toBe(tenancy.version);
});
it('issues a tenant invitation and keeps its token out of URLs and browser storage', async () => {
  const original = handle;
  handle = (url, options) => options.method === 'POST' ? json({ token: 'A'.repeat(64), expiresUtc: '2026-10-25T12:00:00Z' }, 202) : original(url, options);
  open('/app/manage/tenancies/new'); await screen.findByLabelText('Property');
  change('Property', '7'); change('Exact tenant account email or login', 'resident@example.test');
  fireEvent.click(screen.getByRole('button', { name: 'Create invitation' }));
  expect(await screen.findByRole('heading', { name: 'Invitation created' })).toBeTruthy();
  expect((screen.getByLabelText('Invitation token') as HTMLInputElement).value).toBe('A'.repeat(64));
  expect(location.href).not.toContain('AAAA'); expect(localStorage.length).toBe(0); expect(sessionStorage.length).toBe(0);
});
it('accepts an invitation through the tenant API without adding the token to navigation', async () => {
  roles = ['Tenant']; open('/app/tenancies/accept'); await screen.findByLabelText('Invitation token');
  change('Invitation token', 'B'.repeat(64)); fireEvent.click(screen.getByRole('button', { name: 'Accept invitation' }));
  await waitFor(() => expect(post('POST')).toBeTruthy());
  expect(JSON.parse(post('POST')![1].body)).toEqual({ token: 'B'.repeat(64) });
  expect(location.href).not.toContain('BBBB');
});
it('filters the maintenance queue and preserves filters in the API call', async () => {
  open('/app/manage/maintenance'); await screen.findByRole('link', { name: 'Review request #8' });
  change('Status', 'InProgress'); change('Property number', '7'); fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }));
  await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url.includes('status=InProgress') && url.includes('propertyId=7'))).toBe(true));
});
it('uses server-allowed transitions and revision when triaging maintenance', async () => {
  open('/app/manage/maintenance/8'); await screen.findByLabelText('Status');
  expect(screen.queryByRole('option', { name: 'Closed' })).toBeNull();
  change('Status', 'InProgress'); change('Priority', 'High'); change('Note (visible to the tenant)', 'Fictional repair scheduled');
  fireEvent.click(screen.getByRole('button', { name: 'Update request' }));
  await waitFor(() => expect(post('POST')).toBeTruthy());
  expect(JSON.parse(post('POST')![1].body)).toEqual({ expectedRevision: 4, newStatus: 'InProgress', priority: 'High', note: 'Fictional repair scheduled' });
});
it('returns a manual password-verification link for stale admin verification without replaying roles', async () => {
  roles = ['Admin']; handle = (_url, options) => options.method === 'PUT' ? json({ code: 'reauthentication_required', detail: 'private' }, 403) : json(users);
  open('/app/admin/users'); await screen.findByText('account@example.test');
  fireEvent.click(screen.getByRole('checkbox', { name: 'Landlord' })); fireEvent.click(screen.getByRole('button', { name: 'Save roles for account@example.test' }));
  const link = await screen.findByRole('link', { name: 'Verify password, then return and submit again' });
  expect(link.getAttribute('href')).toContain('returnUrl=%2Fapp%2Fadmin%2Fusers');
  expect(screen.getByRole('heading', { name: 'User roles' })).toBeTruthy();
  expect(fetchMock.mock.calls.filter(([, options]) => options.method === 'PUT')).toHaveLength(1);
  expect(document.body.textContent).not.toContain('private');
});
it('signs out with CSRF and clears private property data', async () => {
  const original = handle; handle = (url, options) => { if (url.endsWith('/logout')) signedIn = false; return original(url, options); };
  open('/app/manage'); await screen.findByRole('heading', { name: 'Willow House' });
  fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
  await screen.findByText('Sign in to access your RentRight account.');
  expect(screen.queryByRole('heading', { name: 'Willow House' })).toBeNull();
  expect(post('POST')![1].headers.get('X-CSRF-TOKEN')).toBe('test-csrf');
});
it('aborts a manager read after session loss and ignores a late private response', async () => {
  let resolve!: (response: Response) => void;
  handle = () => new Promise<Response>(done => { resolve = done; });
  open('/app/manage'); await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => url === '/api/v1/workspace')).toBe(true));
  const signal = fetchMock.mock.calls.find(([url]) => url === '/api/v1/workspace')![1].signal;
  signedIn = false; roles = []; fireEvent.focus(window); await screen.findByText('Sign in to access your RentRight account.');
  expect(signal.aborted).toBe(true); resolve(json(summary));
  await waitFor(() => expect(screen.queryByRole('heading', { name: 'Willow House' })).toBeNull());
});
