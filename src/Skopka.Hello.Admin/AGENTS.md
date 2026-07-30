# Admin Module Instructions

Read `../../AGENTS.md` first.

This module is deferred in the first vertical. Admin queries must use
`IIdentityUserQueryService<TProfile>`, never `IQueryable` or EF entities.
Authentication, role/policy authorization and any step-up requirement are
separate mandatory checks. Return only profile fields the current administrator
may inspect.
