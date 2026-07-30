# Facade Module Instructions

Read `../../AGENTS.md` first.

This package owns `AddSkopkaHello<TProfile>()`, shared
`IHelloIdentityApplication<TProfile>` orchestration, secure session-cookie
transport, trusted request context and security-event/outbox contracts. Keep
the public surface small and return the Skopka.Identity builder so persistence,
credentials and tokens remain explicit choices.

This package also owns account-message orchestration and the optional bounded
background SMTP adapter. Public request operations must suppress user-not-found
and delivery outcomes. The built-in queue is intentionally best-effort; durable
delivery remains a replaceable host concern.

Do not add endpoint DTOs, EF entities, OAuth protocol handling or identity
business rules here. Security-event observers are post-commit and must never
claim durable or transactional delivery.
