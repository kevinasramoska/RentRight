import { expect, it } from 'vitest';
import { createDemoRequest } from './demo';
import type { Dashboard } from '../api/tenant';

it('keeps every demo session isolated and its seed immutable to callers', async () => {
  const first = createDemoRequest(), second = createDemoRequest();
  const data = await first<Dashboard>('/api/v1/tenant-dashboard');
  data.tenancies[0]!.propertyTitle = 'Changed outside the demo';
  await first('/api/v1/maintenance-requests', { method: 'POST', body: { description: 'A fictional leaking kitchen tap' } });
  expect((await first<Dashboard>('/api/v1/tenant-dashboard')).counts.open).toBe(4);
  const reset = await second<Dashboard>('/api/v1/tenant-dashboard');
  expect(reset.counts.open).toBe(3);
  expect(reset.tenancies[0]!.propertyTitle).toBe('The Willow, Apartment 4B');
});

it('rejects invalid descriptions and unsupported mutations', async () => {
  const request = createDemoRequest();
  await expect(request('/api/v1/maintenance-requests', { method: 'POST', body: { description: 'short' } })).rejects.toMatchObject({ status: 400 });
  await expect(request('/api/v1/maintenance-requests/1048', { method: 'DELETE' })).rejects.toMatchObject({ status: 404 });
  await expect(request('/api/v1/maintenance-requests/99999')).rejects.toMatchObject({ status: 404 });
});

it('respects cancellation and validates paging', async () => {
  const request = createDemoRequest();
  const controller = new AbortController(); controller.abort();
  await expect(request('/api/v1/tenant-dashboard', { signal: controller.signal })).rejects.toMatchObject({ name: 'AbortError' });
  await expect(request('/api/v1/maintenance-requests?pageSize=1000')).rejects.toMatchObject({ status: 400 });
});
