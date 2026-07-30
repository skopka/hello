# Facade Module Instructions

Read `../../AGENTS.md` first.

This package owns `AddSkopkaHello<TProfile>()`, secure host defaults, trusted
request context and security-event/outbox contracts. Keep the public surface
small and return the Skopka.Identity builder so persistence, credentials and
tokens remain explicit choices.

Do not add endpoint DTOs, EF entities, OAuth protocol handling or identity
business rules here. Security-event observers are post-commit and must never
claim durable or transactional delivery.
