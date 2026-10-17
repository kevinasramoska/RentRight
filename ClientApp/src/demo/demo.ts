import { ApiError } from '../api/client';
import type { RequestOptions } from '../api/client';
import type { Dashboard, Detail, MaintenanceItem } from '../api/tenant';

// Fictional, in-memory presentation data. This module never calls fetch or writes to storage.
export function createDemoRequest() {
  const property = { propertyTitle: 'The Willow, Apartment 4B', propertyAddress: '24 Willow Lane · Demo address', startDate: '2026-02-01', endDate: '2027-01-31' };
  const descriptions = [
    'Kitchen tap is dripping', 'Living room radiator not heating', 'Bedroom window handle is loose',
    'Bathroom extractor fan is noisy', 'Hallway light needs replacing', 'Front door seal repaired',
  ];
  const statuses = ['InProgress', 'Submitted', 'Submitted', 'Resolved', 'Closed', 'Closed'];
  let items: MaintenanceItem[] = descriptions.map((description, i) => ({
    id: 1048 - i, propertyTitle: property.propertyTitle, propertyAddress: property.propertyAddress,
    description, status: statuses[i]!, priority: i === 1 ? 'High' : 'Normal',
    submittedUtc: `2026-10-${String(16 - i * 2).padStart(2, '0')}T09:30:00Z`,
    lastUpdatedUtc: `2026-10-${String(17 - i * 2).padStart(2, '0')}T10:15:00Z`,
  }));

  return async function demoRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
    options.signal?.throwIfAborted();
    const url = new URL(path, 'https://demo.invalid');
    const method = options.method ?? 'GET';
    if (url.pathname === '/api/v1/tenant-dashboard' && method === 'GET') {
      const dashboard: Dashboard = {
        tenancies: [property], counts: { open: items.filter(i => ['Submitted', 'InProgress'].includes(i.status)).length, recent: items.length },
        recentMaintenanceRequests: items.slice(0, 4).map(({ id, propertyTitle, submittedUtc, status }) => ({ id, propertyTitle, submittedUtc, status })),
      };
      return structuredClone(dashboard) as T;
    }
    if (url.pathname === '/api/v1/maintenance-requests' && method === 'POST') {
      const body = options.body as { description?: unknown } | undefined;
      if (typeof body?.description !== 'string' || body.description.trim().length < 10 || body.description.trim().length > 2000)
        throw new ApiError(400, { code: 'validation_failed', errors: { description: ['Invalid description.'] } });
      const id = Math.max(...items.map(i => i.id)) + 1;
      items = [{ id, propertyTitle: property.propertyTitle, propertyAddress: property.propertyAddress,
        description: body.description.trim(), status: 'Submitted', priority: 'Normal',
        submittedUtc: '2026-10-17T14:00:00Z', lastUpdatedUtc: '2026-10-17T14:00:00Z' }, ...items];
      return { id } as T;
    }
    if (url.pathname === '/api/v1/maintenance-requests' && method === 'GET') {
      const page = Number(url.searchParams.get('page') ?? 1);
      const pageSize = Number(url.searchParams.get('pageSize') ?? 10);
      if (!Number.isInteger(page) || page < 1 || page > 10000 || !Number.isInteger(pageSize) || pageSize < 1 || pageSize > 100)
        throw new ApiError(400, { code: 'validation_failed' });
      return structuredClone({ items: items.slice((page - 1) * pageSize, page * pageSize), page, pageSize, totalCount: items.length, totalPages: Math.max(1, Math.ceil(items.length / pageSize)) }) as T;
    }
    const id = /^\/api\/v1\/maintenance-requests\/([1-9]\d*)$/.exec(url.pathname)?.[1];
    const item = items.find(i => i.id === Number(id));
    if (item && method === 'GET') {
      const history: Detail['history'] = [{ status: 'Submitted', occurredUtc: item.submittedUtc, isLegacyBaseline: false }];
      if (item.status !== 'Submitted') history.push({ status: 'InProgress', previousStatus: 'Submitted', occurredUtc: item.lastUpdatedUtc, isLegacyBaseline: false,
        note: item.id === 1048 ? 'A maintenance visit has been arranged. We’ll confirm the time with you before arriving.' : 'The maintenance team has picked up this request.' });
      if (['Resolved', 'Closed'].includes(item.status)) history.push({ status: 'Resolved', previousStatus: 'InProgress', occurredUtc: item.lastUpdatedUtc, isLegacyBaseline: false, note: 'The repair is complete.' });
      if (item.status === 'Closed') history.push({ status: 'Closed', previousStatus: 'Resolved', occurredUtc: item.lastUpdatedUtc, isLegacyBaseline: false });
      return structuredClone({ request: item, history } satisfies Detail) as T;
    }
    throw new ApiError(404, { code: 'resource_not_found' });
  };
}
