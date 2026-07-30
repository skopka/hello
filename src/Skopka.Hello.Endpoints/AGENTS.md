# Endpoint Module Instructions

Read `../../AGENTS.md` first.

This package owns Minimal API routes, HTTP DTOs and the single
OperationResult-to-ProblemDetails mapper. Shared application orchestration and
refresh/antiforgery cookie mechanics live in `Skopka.Hello` so Razor UI and API
cannot diverge.

Call only Skopka.Identity application services. Never inject stores or
DbContexts. Keep login responses enumeration-safe, never serialize a refresh
token, derive client context server-side and require authorization independently
from any future step-up decision.

Password-reset and email-confirmation request endpoints return `202` for known
and unknown well-formed addresses. Token application endpoints map structured
Identity failures normally. Do not put action tokens, recipient addresses or
passwords in logs or response details.
