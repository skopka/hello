# Admin Module Instructions

Read `../../AGENTS.md` first.

Admin queries must use
`IIdentityUserQueryService<TProfile>`, never `IQueryable` or EF entities.
Authentication, role/policy authorization and any step-up requirement are
separate mandatory checks. Return only profile fields the current administrator
may inspect.

The implemented user actions are block, unblock, soft delete, restore and
session revocation. API and Razor UI call the same `IHelloAdminApplication`.
Every mutation uses an Identity-owned OTP proof bound to actor, target, action,
optimistic version and action parameters. Do not add role-list UI by reaching
into stores; wait for a bounded public Identity role-query contract.
