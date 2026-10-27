# Azure free-tier demo deployment

A public portfolio demo on Azure free tiers. It is **not** a production deployment: it holds only
synthetic data, has no SLA, and keeps the limitations listed below.

| Part | Azure resource | Free-tier behaviour |
|---|---|---|
| ASP.NET Core app + React assets | App Service **F1** (Linux, `DOTNETCORE\|10.0`) | Always-free plan; sleeps when idle, 60 CPU-minutes per day |
| Database | Azure SQL Database **free offer** (General Purpose serverless) | Monthly vCore-second allowance; auto-pauses when idle and **pauses until next month** if the allowance runs out |
| App → SQL identity | User-assigned managed identity | No SQL passwords anywhere |
| GitHub → Azure | Microsoft Entra app with an OIDC federated credential | No stored client secret |

```text
GitHub Actions (OIDC) ──► Bicep (infra/main.bicep) ──► App Service F1 ──(managed identity, Entra-only TLS)──► Azure SQL (free)
        │                                                      ▲
        ├─ EF migrations bundle ── temporary runner firewall ──┘
        └─ zip deploy + /health/ready smoke test
```

## What the pipeline does

[`deploy-azure.yml`](../.github/workflows/deploy-azure.yml) runs after **Continuous Integration**
succeeds for a push to `main` (or manually on `main`). It never deploys a pull request or an unchecked commit.

1. Builds React and publishes the app (locked restore, Release), then builds the reviewed
   [EF migrations bundle](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying#bundles).
   The raw SQL scripts are not used: they fail on a fresh database (see [DEPLOYMENT.md](DEPLOYMENT.md)).
2. Signs in to Azure with OIDC and applies [`infra/main.bicep`](../infra/main.bicep) (idempotent).
3. Adds a firewall rule for the runner's IP only, applies migrations as a member of the SQL admin
   group, and grants the app identity `db_datareader` + `db_datawriter`
   ([`GrantAppDatabaseAccess.cs`](../scripts/azure/GrantAppDatabaseAccess.cs)). The rule is removed even on failure.
   The app identity has no DDL rights; only the pipeline migrates.
4. Zip-deploys the app and waits until `/health/ready` reports `Healthy`, then checks
   `/health/live` and the home page.

Pull requests that change `infra/`, `scripts/azure/` or the workflow only compile and lint the Bicep.

## Health endpoints

| Path | Checks | Response |
|---|---|---|
| `/health/live` | Process serves HTTP | `200 Healthy` |
| `/health/ready` | SQL connectivity, 5-second bound | `200 Healthy` or `503 Unhealthy` |

Both are anonymous, uncached GETs that return only the status word: no check names, timings,
exceptions or configuration. Optional geocoding is not part of readiness.

## One-time setup

You need an Azure subscription you own (a free account works) and admin access to this GitHub repository.

1. **Budget guard.** In the Azure portal, open *Cost Management → Budgets* and create a monthly
   budget of 1 (in your billing currency) with an email alert at 100%. Nothing here should cost money;
   the budget tells you if that ever changes.
2. **Bootstrap identities.** Open [Azure Cloud Shell](https://shell.azure.com) (Bash) and run:

   ```bash
   git clone https://github.com/kevinasramoska/RentRight.git
   bash RentRight/scripts/azure-bootstrap.sh
   ```

   It registers the resource providers and creates, if missing: the `rg-rentright-demo` resource group
   (North Europe), the `rentright-github-deploy` Entra app with a federated credential for the
   `azure-demo` GitHub environment only, Contributor on that resource group only, and the
   `rentright-sql-admins` Entra group containing the deploy identity and you. It prints six values.
3. **GitHub environment.** In the repository, open *Settings → Environments → New environment*,
   name it `azure-demo`, restrict deployment branches to `main`, and add the six printed values as
   **environment variables** (they are identifiers, not secrets).
4. **First deploy.** Run *Actions → Deploy Azure demo → Run workflow* on `main`. The first run creates the
   SQL server and database, so it takes longer. The job summary and environment show the live URL.

## Demo accounts

Production intentionally ignores the development `SeedAdmin` settings. Register accounts through the
site, using fictional names and throwaway addresses. To make one an administrator, sign in to the
database as a `rentright-sql-admins` member (for example the portal's *Query editor*, Microsoft Entra
authentication) and add the user to the `Admin` role in `AspNetUserRoles`. Never import real tenancy data.

## Operating limits and decisions

- **Cold starts.** F1 stops idle apps and SQL auto-pauses; the first request after a quiet period can take
  about a minute. The smoke test allows five minutes.
- **Free SQL allowance.** `freeLimitExhaustionBehavior` is `AutoPause`: when the monthly allowance is used,
  the database stays paused until the next month instead of billing. Keep it that way.
- **Network.** F1 has no virtual-network integration, so the SQL firewall admits Azure-hosted callers
  (`AllowAllWindowsAzureIps`). Authentication is Microsoft Entra only, so there is no password to guess.
- **Forwarded headers.** App Service terminates TLS. `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` lets the app see
  HTTPS and the client address that the platform appends (forward limit 1), so rate limits partition per
  client rather than per front end. Direct, un-proxied access to the app is not possible on App Service.
- **Data-protection keys.** No `Security:DataProtection` settings are supplied, so ASP.NET Core uses App Service's
  default key store under `/home/ASP.NET/DataProtection-Keys`. It survives restarts and redeploys of this single
  instance but is not encrypted with a certificate. Sign-in cookies and antiforgery tokens therefore survive
  restarts; a production deployment should supply the certificate-protected key ring described in
  [BROWSER_SECURITY.md](BROWSER_SECURITY.md).
- **Scale.** One instance only. Rate limiting is in-memory per process.
- **Backups and rollback.** The free database keeps Azure's automatic short-term backups with locally redundant
  storage. Roll the app back by re-running the workflow on an earlier `main` commit; migrations are forward-only.

## Tear down

Delete the `rg-rentright-demo` resource group. Optionally delete the `rentright-github-deploy` app registration and
the `rentright-sql-admins` group in Microsoft Entra ID, and the `azure-demo` GitHub environment.
