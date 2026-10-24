# RentRight tenant portal showcase

The resident workspace uses a deep-green sidebar, warm neutral canvas, summary
cards, a locally drawn property illustration, status badges, and a chronological
request timeline. Overview, request list, detail, and submission share the same
components in the authenticated portal and the portfolio demo.

## Try the interactive demo

With Node 24 selected:

```sh
cd ClientApp
npm ci
npm run demo
```

Open <http://127.0.0.1:4173/app/demo/>. The preview binds to loopback only. It is
independent of ASP.NET, SQL Server, accounts, passwords, certificates, and local
user secrets. The normal authenticated portal still requires the ASP.NET host.

Explore overview, the request list, a request's timeline, and the new-request
form. A valid fictional submission opens its simulated detail and updates the
overview count. Refreshing the page resets the data. The `Portfolio demo` badge,
fictional address, resident label, and submission disclosure identify the demo.

Demo data exists only in memory. The demo adapter makes no HTTP requests, stores
no browser data, does not authenticate a user, and cannot mutate a real request.
All live API calls still use the existing cookie, antiforgery, and resource
authorization rules. Leaving the demo rechecks the live session. Demo data is
never used as a fallback when a live request fails. Please enter fictional text.
The scenario uses fixed October 2026 dates so the example remains reproducible.

## Guided portfolio journey

Select **Show walkthrough** for four linked steps: home, request list, repair
timeline, and fictional submission. The guide stays open across client navigation;
**Hide walkthrough** hides only the guide, not an unfinished form or simulated
requests. It starts collapsed so the actual workspace remains the focus on small
screens. Deep links still work without starting a tour.

**Reset demo** first offers **Keep exploring** or **Reset and start again**. Cancel
preserves the current route, form text, and requests and restores button focus.
Confirm replaces only the in-memory adapter, clears unfinished text, collapses
the guide, and returns to the original overview, including when already on that
route. The routed heading receives focus. Old simulated request links then become
unavailable; browser navigation does not resurrect the discarded request data.
No live account, API, cookie, storage, database, or hosting setting is changed.

For a concise explanation of the product, decisions, and evidence, use the
[portfolio case study](PORTFOLIO_CASE_STUDY.md).

## Screenshots for the portfolio

These are actual browser captures of the running UI with synthetic data:

| Asset | Suggested use | Alt text |
|---|---|---|
| [Overview, desktop](showcase/overview-desktop.jpg) | Main project screenshot | RentRight resident overview with rental details, maintenance counts, and recent requests |
| [Requests, desktop](showcase/requests-desktop.jpg) | Feature gallery | Maintenance request list with submitted, in-progress, resolved, and closed status badges |
| [Timeline, desktop](showcase/timeline-desktop.jpg) | Workflow detail | Maintenance request details and a chronological progress timeline |
| [New request, desktop](showcase/new-request-desktop.jpg) | Feature gallery | A labelled maintenance description form with guidance and a simulated submission notice |
| [Overview, mobile](showcase/overview-mobile.jpg) | Responsive-design evidence | RentRight overview adapted to a narrow mobile screen |
| [Guided overview, desktop](showcase/guided-overview-desktop.png) | Current guided-demo hero | Resident overview with an expanded four-step demo guide and synthetic tenancy data |
| [Guided timeline, desktop](showcase/guided-timeline-desktop.png) | Current workflow detail | Guided demo showing a kitchen-tap repair and chronological property-team updates |
| [Guided overview, mobile](showcase/guided-overview-mobile.png) | Current responsive evidence | Mobile resident overview with compact walkthrough and reset controls |

Keep the demo disclosure visible when presenting screenshots. These assets show
the tenant experience. Landlord and administrator business workflows are available
in the authenticated React workspaces, but are not simulated or represented by
this screenshot set. Identity account screens remain server-rendered.

## Static export and hosting

```sh
cd ClientApp
npm run demo:build
```

The standalone output is `ClientApp/dist/`, containing the HTML host, hashed
JavaScript/CSS, fonts, and licenses. Build output is ignored by Git. To host the
demo on a portfolio or separate domain, mount this output at `/app/` and rewrite
extensionless `/app/demo` routes to `/app/index.html`. Serve actual `/app/assets`
and `/app/fonts` files normally; missing assets must not fall back to HTML.
The public entry is `/app/demo/`, not the authenticated `/app/` entry.

No hosting or portfolio-website deployment is performed by this change. Test the
chosen host's base path and deep-link rewrites before adding a public demo link.
A different deployment prefix requires an intentional route/base-path update.

The earlier local handoff archive was `artifacts/rentright-portfolio-kit.zip`. It contained
the screenshots, the static build mounted under `demo/app/`, this report, and
example static-host rewrite rules. It is a generated artifact, not tracked source;
it is not refreshed by this follow-up. Use the current screenshots and a fresh
static build rather than treating that earlier archive as the current release.

## Accessibility and verification

The layout retains semantic landmarks, labelled forms, linked errors, keyboard
focus, fixed safe failure messages, and escaped descriptions/notes. The mobile
drawer is a labelled modal with a focus loop, Escape dismissal, focus restoration,
and an inert background. Reduced-motion preferences are respected.

Frontend checks cover the existing live journey plus demo isolation, simulated
submission, returning to a live session, reset semantics, cancellation, and drawer
keyboard behavior. Browser checks exercise navigation, submission, desktop and
mobile layout, drawer dismissal, and horizontal overflow. This does not replace a
screen-reader audit or broad device testing. No server or schema changes are made.

Verified on 2026-10-17 with Node 24: lint, strict TypeScript, **54 frontend tests**,
normal/static builds, and npm audit (**0 vulnerabilities**). Release .NET build
passed with **0 warnings/errors** and all **388 fast .NET tests** passed. The SQL
suite was not rerun because no server, query, model, or database behavior changed.
Browser checks covered 320/390px mobile, 768px tablet, and 1440px desktop widths;
the sampled overview/detail screens had no horizontal overflow.

Pre-merge review on 2026-10-18 brought the branch up to date with the browser and
account security changes. All 62 combined frontend tests, 499 fast backend tests,
lint, type checking, static builds and the clean Release solution build passed.
The review restored visible focus outlines for routed headings and the skip-link
target and enabled vertical scrolling for the fixed desktop sidebar. Browser
checks confirmed 3px focus indicators on both targets and that, at 1440×420, the
723px sidebar scrolls to expose its lower source link. No live API or database is
needed by this preview. Earlier screenshots remain representative of the design.

## Files changed

### Guided-demo follow-up (2026-10-23)

- `ClientApp/src/demo/DemoGuide.tsx`: optional tour, current-step semantics, reset confirmation and cancel focus.
- `ClientApp/src/App.tsx`: replace only the demo adapter on confirmed reset; preserve live session/API behavior.
- `ClientApp/src/demo/DemoJourney.test.tsx`: six integration-style demo/navigation/isolation regressions.
- `ClientApp/src/style.css`: scoped guide/reset styling with responsive controls and existing visible focus treatment.
- `ClientApp/index.html`, `ClientApp/public/favicon.svg`: locally served icon so standalone preview does not request the absent backend favicon.
- `docs/showcase/guided-overview-desktop.png`, `docs/showcase/guided-timeline-desktop.png`, `docs/showcase/guided-overview-mobile.png`: current unedited synthetic-data browser captures.
- `docs/PORTFOLIO_CASE_STUDY.md`: short product/architecture/evidence narrative with reproducible demo steps and explicit limits.
- `README.md`, `docs/TASKS.md`, this report: entry points, scope decision and verification handoff.

Verified locally: 91 frontend tests, lint, strict TypeScript, normal/static builds,
and audit with no reported vulnerabilities. Browser observations include the
full demo journey, confirmed/cancelled reset, zero API/failed requests, desktop and
320/390/768px responsive overflow checks, keyboard activation, drawer Escape and
focus restoration. The local icon returns 200 as `image/svg+xml` under `/app/`.
The Release .NET solution build passes with zero warnings/errors and all 596
fast backend tests pass. No server, schema, dependency, or public API contract
changed; the SQL suite is not part of this local frontend-only verification.

### Original showcase

- `ClientApp/src/App.tsx`: selects the explicit demo entry and retains live-session handling.
- `ClientApp/src/Portal.tsx`: presents overview, request list, timeline, form, and empty/error states with a replaceable request adapter.
- `ClientApp/src/components/Workspace.tsx`: shared sidebar, header, profile, footer, and accessible mobile navigation.
- `ClientApp/src/components/Icon.tsx`: local SVG icons and a decorative building illustration.
- `ClientApp/src/style.css`: responsive workspace styling, focus treatment, status tones, and local font faces.
- `ClientApp/src/api/client.ts`: exports the existing request-options type for the demo adapter; runtime behavior is unchanged.
- `ClientApp/src/demo/demo.ts`: isolated fictional dataset and simulated request operations.
- `ClientApp/src/App.test.tsx`, `ClientApp/src/demo/demo.test.ts`: live-journey regressions and demo/navigation tests.
- `ClientApp/package.json`: local demo and static-export commands; no dependencies added.
- `ClientApp/vite.config.ts`: allows static preview without development HTTPS certificates; authenticated development proxy settings remain intact.
- `ClientApp/public/fonts/`: original variable fonts and their SIL Open Font License notices.
- `docs/showcase/`: the five browser-captured JPEG screenshots listed above.
- `README.md`, `docs/TASKS.md`, this report: document the presentation work and demo boundaries.

## Font provenance

Unmodified Manrope and DM Sans variable fonts are bundled from the official
[Google Fonts Manrope directory](https://github.com/google/fonts/tree/main/ofl/manrope)
and [DM Sans directory](https://github.com/google/fonts/tree/main/ofl/dmsans).
Their original copyright and SIL Open Font License 1.1 notices are retained next
to the font files and copied into the build. No third-party font requests are
needed at runtime. Illustrations and icons are code-native SVGs in this project.
