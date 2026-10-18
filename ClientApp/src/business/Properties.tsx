import { ApiError, apiRequest } from '../api/client';
import type { ApiPage } from '../api/client';
import { ActionForm, LocationCard, PageHeading, Paging, Remote, money, pageNumber, write } from './ui';
import type { ManagedProperty, PageProps, Property, WorkspaceSummary } from './ui';

const catalog = '/api/v1/properties';
export function PublicProperties({ route, navigate, onAccessLost, manager }: PageProps & { manager: boolean }) {
  const url = new URL(route, location.origin);
  const id = /^\/app\/properties\/([1-9]\d*)\/?$/.exec(url.pathname)?.[1];
  const query = new URLSearchParams({ page: String(pageNumber(route)), pageSize: '12' });
  for (const name of ['search', 'minimumRent', 'maximumRent']) { const value = url.searchParams.get(name); if (value) query.set(name, value); }
  return <><PageHeading title={id ? 'Property details' : 'Find your next home'}>{manager && <a className="button button-primary" href="/app/manage/properties/new">Add property</a>}</PageHeading>
    {id ? <Remote<ManagedProperty> path={`/api/v1/workspace/published-properties/${id}`} onAccessLost={onAccessLost}>{property => <>
      <a className="back-link" href="/app/properties">← Browse properties</a><section className="business-card"><p className="eyebrow">A PLACE TO CALL HOME</p><h2>{property.title}</h2><p>{property.address}</p><strong className="property-rent">{money(property.rentAmount)} / month</strong><p className="business-text">{property.description || 'No additional description.'}</p></section>
      <LocationCard location={property.location} />{manager && property.canManage && <a className="button button-secondary" href={`/app/manage/properties/${id}`}>Manage this property</a>}
    </>}</Remote> : <>
      <form className="business-card filter-grid" onSubmit={event => { event.preventDefault(); const data = new FormData(event.currentTarget); const values = new URLSearchParams(); for (const name of ['search', 'minimumRent', 'maximumRent']) { const value = String(data.get(name) ?? '').trim(); if (value) values.set(name, value); } navigate('/app/properties?' + values); }}>
        <label>Search title or address<input name="search" maxLength={200} defaultValue={url.searchParams.get('search') ?? ''} /></label>
        <label>Minimum monthly rent<input name="minimumRent" type="number" min="0" step="0.01" defaultValue={url.searchParams.get('minimumRent') ?? ''} /></label>
        <label>Maximum monthly rent<input name="maximumRent" type="number" min="0" step="0.01" defaultValue={url.searchParams.get('maximumRent') ?? ''} /></label><button className="button button-primary">Search properties</button>
      </form><Remote<ApiPage<Property>> path={`${catalog}?${query}`} onAccessLost={onAccessLost}>{data => <>
        {data.items.length ? <div className="property-grid">{data.items.map(property => <PropertyCard key={property.id} property={property} base="/app/properties" />)}</div> : <div className="empty-state"><h2>No matching properties</h2><p>Try a different address, title or rent range.</p></div>}
        <Paging data={data} route={route} />
      </>}</Remote></>}
  </>;
}
export function PropertyCard({ property, base = '/app/manage/properties' }: { property: Property; base?: string }) {
  return <article className="business-card property-card"><p className="eyebrow">PROPERTY #{property.id}</p><h2><a href={`${base}/${property.id}`}>{property.title}</a></h2><p>{property.address}</p><strong className="property-rent">{money(property.rentAmount)} / month</strong><a className="text-link" href={`${base}/${property.id}`}>View property →</a></article>;
}
export function ManagedProperties({ route, navigate, onAccessLost, admin }: PageProps & { admin: boolean }) {
  const path = route.split('?')[0]?.replace(/\/$/, '');
  const id = /^\/app\/manage\/properties\/([1-9]\d*)(?:\/edit|\/delete)?$/.exec(path ?? '')?.[1];
  const form = path?.endsWith('/edit'), deleting = path?.endsWith('/delete'), creating = path === '/app/manage/properties/new';
  if (creating) return <><PageHeading title="Add property" /><Remote<WorkspaceSummary> path="/api/v1/workspace" onAccessLost={onAccessLost}>{data => <PropertyForm enabled={data.locationLookupEnabled} navigate={navigate} onAccessLost={onAccessLost} />}</Remote></>;
  if (id) return <><PageHeading title={form ? 'Edit property' : deleting ? 'Delete property' : 'Manage property'} /><Remote<ManagedProperty> path={`/api/v1/workspace/properties/${id}`} onAccessLost={onAccessLost}>{(property, reload) => form ? <PropertyForm property={property} enabled={property.locationLookupEnabled} navigate={navigate} onAccessLost={onAccessLost} /> : deleting ? <section className="business-card"><h2>Delete {property.title}?</h2><p>This is permanent. Properties with tenancy history cannot be deleted.</p><ActionForm danger submitLabel="Confirm property deletion" onAccessLost={onAccessLost} onSuccess={() => navigate('/app/manage/properties')} submit={(_, signal) => write(`${catalog}/${id}`, { method: 'DELETE', expectedVersion: property.version }, signal)}><label className="check-label"><input type="checkbox" required />I understand this permanently deletes this property.</label></ActionForm><a className="text-link" href={`/app/manage/properties/${id}`}>Cancel</a></section> : <>
    <section className="business-card"><h2>{property.title}</h2><p>{property.address}</p><strong className="property-rent">{money(property.rentAmount)} / month</strong><p className="business-text">{property.description}</p><div className="business-actions"><a className="button button-primary" href={`/app/manage/properties/${id}/edit`}>Edit property</a><a className="button button-secondary" href={`/app/manage/properties/${id}/delete`}>Delete property</a><a className="text-link" href="/app/manage/tenancies/new">Invite a tenant</a></div></section><LocationCard location={property.location} />
    {property.locationLookupEnabled && <section className="business-card"><h2>Find or refresh map location</h2><p>Sends the saved address to Geoapify. Cached results may be reused.</p><ActionForm onAccessLost={onAccessLost} submitLabel="Find or refresh location" submit={(_, signal) => write(`/api/v1/workspace/properties/${id}/location`, { method: 'POST', body: { expectedVersion: property.version } }, signal)} onSuccess={reload} /></section>}
  </>}</Remote></>;
  return <><PageHeading title={admin ? 'All properties' : 'Your properties'}><a className="button button-primary" href="/app/manage/properties/new">Add property</a></PageHeading>
    {admin ? <Remote<ApiPage<Property>> path={`${catalog}?page=${pageNumber(route)}&pageSize=12`} onAccessLost={onAccessLost}>{data => <><div className="property-grid">{data.items.map(property => <PropertyCard key={property.id} property={property} />)}</div>{!data.items.length && <p>No properties yet.</p>}<Paging data={data} route={route} /></>}</Remote>
      : <Remote<WorkspaceSummary> path="/api/v1/workspace" onAccessLost={onAccessLost}>{data => <><p className="field-help">Up to 100 owned properties, ordered by title.</p><div className="property-grid">{data.properties.map(property => <PropertyCard key={property.id} property={property} />)}</div>{!data.properties.length && <p>You have no properties yet. Add your first property above.</p>}</>}</Remote>}
  </>;
}
function PropertyForm({ property, enabled, navigate, onAccessLost }: { property?: ManagedProperty; enabled: boolean } & Pick<PageProps, 'navigate' | 'onAccessLost'>) {
  return <section className="business-card"><h2>{property ? property.title : 'A new place to call home'}</h2>{enabled && <p className="notice">New or changed addresses are sent to Geoapify after saving. Do not include tenant names or private information. Lookup failures do not undo a save.</p>}
    <ActionForm onAccessLost={onAccessLost} onSuccess={value => navigate(`/app/manage/properties/${property?.id ?? (value as Property).id}`)} submit={(data, signal) => {
      const rentAmount = Number(data.get('rentAmount'));
      if (!Number.isFinite(rentAmount) || rentAmount < 0) return Promise.reject(new ApiError(400, { code: 'validation_failed' }));
      const body = { title: String(data.get('title') ?? ''), address: String(data.get('address') ?? ''), rentAmount, description: String(data.get('description') ?? '') || null, ...(property ? { expectedVersion: property.version } : {}) };
      return apiRequest<Property>(property ? `${catalog}/${property.id}` : catalog, { method: property ? 'PUT' : 'POST', body, signal });
    }}>
      <label>Title<input name="title" required maxLength={200} defaultValue={property?.title} /></label><label>Address<input name="address" required maxLength={500} defaultValue={property?.address} /></label>
      <label>Monthly rent (€)<input name="rentAmount" type="number" min="0" max="9999999999999999.99" step="0.01" required defaultValue={property?.rentAmount ?? 0} /></label><label>Description<textarea name="description" maxLength={2000} defaultValue={property?.description ?? ''} /></label>
    </ActionForm><a className="text-link" href={property ? `/app/manage/properties/${property.id}` : '/app/manage/properties'}>Cancel</a>
  </section>;
}
