# Admin Module Instructions

Read `../../AGENTS.md` first.

Admin queries must use
`IIdentityUserQueryService<TProfile>`, never `IQueryable` or EF entities.
Authentication, role/policy authorization and any step-up requirement are
separate mandatory checks. Return only profile fields the current administrator
may inspect.

The implemented user actions are block, unblock, soft delete, restore and
session revocation. Role administration uses
`IIdentityRoleQueryService<TProfile>` for bounded listing and
`IIdentityRoleService<TProfile>` for CRUD and membership; never emulate either
with stores. API and Razor UI call the same admin applications. Every mutation
uses an Identity-owned OTP proof bound to actor, target, action, optimistic
version and action parameters. Role mutations require the highest configured
admin policy, protected policy roles cannot be renamed or deleted, and an
actor cannot remove their own protected role. Membership changes revoke the
target user's sessions and must report a committed-mutation cleanup failure
without inviting a replay.

The Admin Razor UI owns a separate full-width layout and locally packaged
Bootstrap assets. Keep third-party assets versioned with their license, never
load them from a CDN, and link host custom CSS after the built-in Admin styles.
Security-sensitive forms remain server-rendered and antiforgery-protected;
visual collapsing must not change their authorization or step-up boundaries.
