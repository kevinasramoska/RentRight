# Phase 1 manual verification

**Historical Phase 1 checklist:** P05-01 supersedes direct assignment and Admin-only tenancy access. Use [TENANCY_LIFECYCLE.md](TENANCY_LIFECYCLE.md) for the current invite/accept/end workflow.

This checklist verifies the MVC tenancy and maintenance workflow added in Phase 1. Use synthetic local accounts and a disposable development database.

## Prerequisites

1. Configure a local SQL Server connection and development Admin credentials with user secrets.
2. Apply migrations:

   ```bash
   dotnet ef database update
   ```

3. Start the application:

   ```bash
   dotnet run --project RentRight.csproj
   ```

4. Register synthetic accounts for:
   - `landlord-a@example.test`
   - `landlord-b@example.test`
   - `tenant-a@example.test`
   - `tenant-b@example.test`
5. Sign in as Admin and assign the corresponding `Landlord` and `Tenant` roles from the Admin Panel.
6. Sign in as each landlord and create one property so the database contains properties owned by different users.

## Tenancy checks

1. Sign in as Admin and open **Dashboard > Manage Tenancies**.
2. Create an active tenancy between Tenant A and Landlord A's property.
3. Confirm the same property is no longer offered for another active tenancy.
4. Try posting an end date before the start date and confirm validation prevents creation.
5. Confirm a non-Admin receives an access-denied response for `/Tenancies` and `/Tenancies/Create`.
6. End the tenancy and confirm it remains visible as `Ended` rather than being deleted.
7. Try creating an active tenancy whose end date is already in the past and confirm validation prevents it.
8. Create a new active tenancy for Tenant A so the maintenance checks can continue.

## Tenant checks

1. Sign in as Tenant A and open the Tenant Dashboard.
2. Confirm only Tenant A's tenancy/property appears and that Tenant B's associations do not appear.
3. Open **Submit Request**, select the associated property, enter at least 10 non-whitespace characters, and submit.
4. Confirm the new request starts at `Submitted` and appears on both the dashboard and **My Requests**.
5. Confirm Tenant A can open the request details.
6. Sign in as Tenant B and manually request Tenant A's request URL. Confirm the application returns `404`.
7. Using browser developer tools, change a create form's `TenancyId` to Tenant B's or an ended tenancy ID. Confirm validation rejects it and no row is created.
8. End Tenant A's tenancy as Admin, return as Tenant A, and confirm historical requests remain visible but a new request cannot be submitted for that tenancy.

## Landlord checks

1. Sign in as Landlord A and open **Property Maintenance Requests**.
2. Confirm requests for Landlord A's properties are present.
3. Update the request through `Submitted -> In Progress -> Resolved -> Closed`.
4. Confirm an invalid skipped transition, such as `Submitted -> Resolved`, is rejected even if the posted value is tampered with.
5. Sign in as Landlord B and manually request the details and status-update URLs for Landlord A's request. Confirm both return `404` and no update occurs.
6. Confirm a landlord cannot access the tenant-only create action unless that account separately has the Tenant role and an active tenancy.

## Admin checks

1. Sign in as Admin and open **Manage Maintenance Requests**.
2. Confirm requests across both landlords and all tenants are visible.
3. Confirm Admin can open and update any request while still following the allowed transition rules.
4. Confirm Admin can create/end tenancies but cannot create a second active tenancy for the same property.

## Property and security regression checks

1. As Landlord B, manually request edit/delete for Landlord A's property and confirm access is forbidden.
2. As an Admin who also has the Landlord role, confirm Admin can still manage another landlord's property and requests.
3. Confirm anonymous and Tenant property pages do not show create/edit/delete controls.
4. Attempt to delete a property with tenancy history and confirm the application refuses with a useful message.
5. Submit each POST form without a valid antiforgery token and confirm it is rejected.
6. Confirm property and maintenance descriptions render as text rather than executable HTML.

## Migration checks

On a disposable database copied from the pre-Phase-1 schema:

1. Add at least one legacy maintenance request with `Status = 'Pending'`.
2. Apply `CompleteCoreRentalDomain`.
3. Confirm a corresponding ended tenancy was created for each legacy property/tenant pair.
4. Confirm each legacy request references that tenancy, has `Submitted` status, and preserves its submission timestamp as `LastUpdatedUtc`.
5. Confirm the migration stops with an actionable message rather than truncating data if legacy strings exceed the new limits or statuses are unsupported.
6. Confirm `dotnet ef migrations has-pending-model-changes` reports no differences after the migration is generated.

## Known edge cases and deliberately deferred behavior

- Tenancy assignment is Admin-only and immediate. Tenant invitation/acceptance is deferred.
- One `Active` tenancy row is allowed per property, including a future-dated tenancy. Multi-unit/multi-occupancy properties are not modelled.
- Ending a future tenancy records its end date as its start date because there is no separate `Cancelled` state yet.
- Status transitions have no immutable history record yet; only current status and `LastUpdatedUtc` are stored.
- Maintenance updates do not yet use rowversion, so simultaneous valid status updates are last-write-wins. Optimistic concurrency remains a later database task.
- Lists are not paginated in Phase 1.
- A simultaneous duplicate-tenancy submission is blocked by the filtered unique index and translated back to the tenancy form; broader concurrency handling remains deferred.
- Date eligibility is evaluated against the current UTC date.
- Migrated legacy maintenance relationships become ended tenancies so migration does not incorrectly assert that old occupancy is still active.
