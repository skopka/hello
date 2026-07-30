# Server Module Instructions

Read `../../AGENTS.md` first.

This executable composes PostgreSQL, the selected password hasher, JWT sessions,
bearer validation, endpoints, health checks and Docker behavior. Keep secrets
outside source and appsettings. Migrations run only behind the explicit
configuration switch and must not race across production replicas.

Action-token links require configured `SkopkaHello:PublicOrigin`. SMTP remains
optional and credentials come from secrets/environment configuration; omitting
the SMTP host keeps the null delivery adapter.

The Server contains host configuration, not reusable identity or endpoint
business logic.
