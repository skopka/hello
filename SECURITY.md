# Security Policy

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability. Report it privately to
the repository maintainers through the security advisory channel of the project
hosting service. Include affected versions, prerequisites, impact and a minimal
reproduction when it is safe to do so.

Maintainers should acknowledge a complete report within seven days. Disclosure
timing is coordinated after impact and remediation are understood.

## Supported versions

Skopka.Hello is currently pre-1.0. Security fixes are applied to the latest
released minor version. Older pre-1.0 builds may require upgrading.

## Scope

Reports about HTTP transport, cookie/CSRF behavior, endpoint authorization,
server composition and Skopka.Hello UI or protocol adapters belong here.
Identity-domain, credential, session-persistence or token-provider findings may
belong to the Skopka.Identity repository.

See [docs/security.md](docs/security.md) for the deployment threat model and
known operational requirements.
