# Server Module Instructions

Read `../../AGENTS.md` first.

This executable composes PostgreSQL, the selected password hasher, JWT sessions,
bearer validation, endpoints, health checks and Docker behavior. Keep secrets
outside source and appsettings. Migrations run only behind the explicit
configuration switch and must not race across production replicas.

The Server contains host configuration, not reusable identity or endpoint
business logic.
