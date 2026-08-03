# Facade Module Instructions

Read `../../AGENTS.md` first.

This package owns `AddSkopkaHello<TProfile>()`, shared
`IHelloIdentityApplication<TProfile>` orchestration, secure session-cookie
transport, trusted request context and security-event/outbox contracts. Keep
the public surface small and return the Skopka.Identity builder so persistence,
credentials and tokens remain explicit choices.

This package also owns channel-aware account-message orchestration, custom
email/SMS provider contracts and the optional bounded background SMTP adapter.
Public request operations must suppress user-not-found and delivery outcomes.
The built-in inbox and SMTP queue are intentionally best-effort. Keep
`IHelloAnonymousAccountMessageInbox` and `IHelloAccountMessageSender`
replaceable so a host can provide durable storage. Step-up selects the
configured confirmed-contact channel before challenge creation and never falls
back across channels.

Do not add endpoint DTOs, EF entities, OAuth protocol handling or identity
business rules here. Security-event observers are post-commit and must never
claim durable or transactional delivery.

This package owns the startup self-registration policy, its shared
`OperationResult` error and the immutable UI route snapshot derived from
`SkopkaHelloOptions.UiPathPrefix`. Enforce the policy before invoking either
Identity registration service. Do not make downstream UI/OIDC packages keep
independent copies of these settings.
