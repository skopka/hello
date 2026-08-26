# Administration

`Skopka.Hello.Admin` supplies bounded user- and role-administration APIs and
Razor pages.
Identity still owns users, roles, optimistic concurrency, verification and
session persistence. The module calls `IIdentityUserQueryService<TProfile>`,
`IIdentityUserService<TProfile>`, `IIdentityRoleQueryService<TProfile>`,
`IIdentityRoleService<TProfile>` and `IIdentitySessionService<TProfile>`; it
does not inject a store, `DbContext` or `IQueryable`.

## Host composition

Enable Identity roles before bearer authentication, register an explicit safe
profile projector, then map the admin endpoints:

```csharp
identity.AddRoles();
identity.UseJwtBearerAuthentication();

services.AddSkopkaHelloAdmin<MyProfile, MyAdminProfileProjector>(options =>
{
    options.ApiPathPrefix = "/admin";
    options.RazorUiEnabled = true;
    options.ReadRoleName = "Skopka.Hello.Admin";
    options.ManageRoleName = "Skopka.Hello.Admin";
    options.DeleteRoleName = "Skopka.Hello.Admin";
    options.Roles.Protect(
        "iq-author",
        HelloRoleProtection.Retained);
    options.Roles.Protect(
        "iq-teacher",
        HelloRoleProtection.Structural);
    options.Roles.GrantableBy(
        "iq-teacher",
        ["Skopka.Hello.Admin", "iq-manager"]);
    options.RoleAssignment.RoleName = "iq-manager";
    options.RoleAssignment.Assignable = ["iq-author", "iq-teacher"];
    options.RoleManagementEnabled = false;
    options.RevokeSessionsOnRoleGrant = false;
    options.RevokeSessionsOnRoleRemoval =
        HelloSessionRevocationScope.ProtectedOnly;
});

app.MapSkopkaHelloAdmin<MyProfile>();
app.MapSkopkaHelloUi();
```

The projector is mandatory because a generic `TProfile` must not be serialized
to an administrator by accident. It receives both actor and target ids, so a
host can redact fields based on the current administrator:

```csharp
public sealed class MyAdminProfileProjector
    : IHelloAdminProfileProjector<MyProfile>
{
    public Task<OperationResult<IReadOnlyList<HelloAdminProfileField>>>
        ProjectAsync(
            MyProfile profile,
            HelloAdminProfileProjectionContext context,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<HelloAdminProfileField> fields =
        [
            new("displayName", "Display name", profile.DisplayName),
        ];
        return Task.FromResult(OperationResultFactory.Success(fields));
    }
}
```

Do not project secrets, internal fraud signals or tenant data the actor is not
allowed to inspect. A projection failure remains an `OperationResult` and is
mapped through the common ProblemDetails/Razor validation contract.

## Authorization model

Authentication, authorization and step-up are three separate gates:

1. API routes require bearer authentication; the Razor page requires the
   protected Hello UI cookie.
2. Every handler explicitly evaluates a read, manage, delete or role-assignment
   policy. The
   built-in policy handler looks up current role membership through
   `IIdentityRoleService<TProfile>` instead of trusting a possibly stale role
   claim.
3. Every mutation requires an Identity-owned step-up. The proof
   is bound to the actor, target user, action, optimistic version, block expiry,
   reason or role-parameter fingerprints, delivery channel and
   confirmed-destination fingerprint.

By default, the administrator must have a confirmed contact for the configured
`SkopkaHello:Delivery:VerificationChannel`. With
`SkopkaHello:Delivery:RequireTotpWhenEnabled=true`, an administrator who has
enabled an authenticator uses its current code or an unused recovery code
instead, with no delivery dependency. A wrong response is retryable; a
terminal verification, changed binding or mutation race requires a new
challenge. The `reset-authenticator` user action lets an authorized
administrator reset another user’s factor and revokes that user’s sessions;
it is itself protected by the actor’s step-up policy.

The ready Server uses these independent policies and role settings:

```json
{
  "SkopkaHello": {
    "Admin": {
      "ApiPathPrefix": "/admin",
      "RazorUiEnabled": true,
      "ReadPolicyName": "Skopka.Hello.Admin.Read",
      "ManagePolicyName": "Skopka.Hello.Admin.Manage",
      "DeletePolicyName": "Skopka.Hello.Admin.Delete",
      "RoleAssignmentPolicyName": "Skopka.Hello.Admin.RoleAssignment",
      "ReadRoleName": "Skopka.Hello.Admin",
      "ManageRoleName": "Skopka.Hello.Admin",
      "DeleteRoleName": "Skopka.Hello.Admin",
      "ProtectedRoleNames": [],
      "RoleAssignment": {
        "RoleName": null,
        "Assignable": [],
        "NotAssignable": []
      },
      "RoleManagementEnabled": true,
      "RevokeSessionsOnRoleGrant": true,
      "RevokeSessionsOnRoleRemoval": "Always"
    }
  }
}
```

Use different role names when read, state-management and deletion privileges
must be separated. Policy names must be distinct. The Razor route is composed
from the Hello UI prefix plus the admin API prefix, so the defaults expose API
under `/admin` and UI under `/hello/admin/users` and `/hello/admin/roles`.
The protected `/hello/admin` entry route redirects to the user list.
Role catalog creation, update and deletion require `DeletePolicyName`.
Membership assignment and removal require `RoleAssignmentPolicyName`; by
default that policy accepts the configured delete role, preserving the old
authorization behavior. A host can additionally set
`RoleAssignment.RoleName` to delegate only membership work without granting
user management or role catalog mutation.
The user page loads the bounded role catalog and offers unassigned roles by
name while posting their identifiers through the existing membership and
step-up contract. When more than 100 roles exist, the field keeps catalog
suggestions and also accepts a role identifier manually; the complete catalog
remains available on the role page.

Call `AddSkopkaHelloUi<TProfile, TProfileFactory>` before the admin registration
when `RazorUiEnabled` is true. API-only hosts can set it to false; no admin
Razor application part or route convention is then installed.

## Bootstrap the first administrator

Create and confirm a normal user first, note its id from the account response,
then run the ready Server once in explicit bootstrap mode:

```powershell
dotnet run --project .\src\Skopka.Hello.Server -- `
  --bootstrap-admin 11111111-1111-1111-1111-111111111111
```

The command creates the configured role or roles when missing, assigns the
existing user and revokes that user's sessions. It does not search by email,
create a user or start the web server. Sign in again after it completes so a
new token/ticket is issued. The operation is idempotent and must run with the
same database and secret configuration as the service.

## API

`GET /admin/users` accepts `search`, `status`, `requiredFlags`, `pageSize`,
`cursorCreatedAt` and `cursorId`. Both cursor values must be supplied together.
Identity caps a page at 100 and orders its opaque continuation by
`(CreatedAt, Id)`.

Mutations use the slugs `block`, `unblock`, `delete`, `restore` and
`revoke-sessions`. Start a challenge with the intended command parameters:

```http
POST /admin/users/{userId}/actions/block/challenge
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "expectedVersion": 7,
  "blockedUntil": "2026-08-05T12:00:00Z",
  "reason": "security review"
}
```

Repeat exactly those parameters with the returned challenge and the delivered
code:

```http
POST /admin/users/{userId}/actions/block
Authorization: Bearer <access-token>
Content-Type: application/json

{
  "challengeId": "22222222-2222-2222-2222-222222222222",
  "verificationCode": "123456",
  "expectedVersion": 7,
  "blockedUntil": "2026-08-05T12:00:00Z",
  "reason": "security review"
}
```

Block and delete revoke every target refresh session after the user-state
mutation. Delete is the Skopka.Identity soft-delete operation. An
administrator cannot block or delete their own account through this surface.

`GET /admin/roles` accepts `search`, `pageSize`, `cursorCreatedAt` and
`cursorId`. Identity caps the page at 100 and orders its continuation by
`(CreatedAt, Id)`. `GET /admin/users/{userId}/roles` returns the target's
current memberships.

Role mutations use `POST /admin/roles/actions/{action}/challenge` followed by
`POST /admin/roles/actions/{action}` with the exact same parameters plus the
challenge id and delivered code. Supported slugs are `create`, `update`,
`delete`, `assign` and `remove`. Create accepts `name`, optional `description`
and optional `parentId`; update additionally requires `roleId` and
`expectedVersion`; delete requires `roleId` and `expectedVersion`; membership
actions require `roleId` and `targetUserId`.

Protect application-defined role names with `Roles.Protect`. Name matching
trims configured values and ignores case:

| Protection | Rename/delete | Remove from self | Remove from another user |
| --- | --- | --- | --- |
| `System` | rejected | rejected | allowed |
| `Retained` | rejected | rejected | allowed |
| `Structural` | rejected | allowed | allowed |

`ReadRoleName`, `ManageRoleName` and `DeleteRoleName` are always implicitly
`System`, even if the same name is explicitly configured with a weaker level.
The legacy `ProtectedRoleNames` option remains supported as a `Retained` alias;
it is empty by default, so existing hosts keep their previous behavior.

Use `Roles.GrantableBy(targetRole, actorRoleNames)` to constrain who may assign
or remove one target role. An empty actor-role list means any actor who already
has role-assignment capability. A non-empty rule is always enforced, including
for the delete-role administrator. The actor-wide delegate filter only narrows
that result:

- `RoleAssignment.Assignable` is an allowlist;
- `RoleAssignment.NotAssignable` is a denylist;
- leaving both empty allows every target role not restricted by its own
  `GrantableBy` rule.

`Assignable` and `NotAssignable` are mutually exclusive, and configuring both
fails during startup. The API rechecks these rules on both challenge creation
and completion, while the Users page omits unavailable assignment/removal
controls. A delegated actor can read the bounded user and role catalogs needed
for membership work but receives no block, delete, session-revocation or role
CRUD controls.

Set `RoleManagementEnabled` to `false` when the host owns the complete role
catalog in code. Role creation, update and deletion are then rejected by both
API and Razor handlers, their Razor forms are hidden, and the role page explains
that the application owns the catalog. Role queries and user membership
assignment/removal remain available; `RoleManagementEnabled` defaults to
`true`.

Removing another administrator's last membership is an explicit
high-privilege operation; if operators lock out every administrator, recover
with the bootstrap command.

Host-side operator commands call Identity services directly and are not
restricted by the interactive `GrantableBy` or delegate filters. This includes
the ready Server's `--bootstrap-admin` command and host-defined commands such as
`--grant-role`, preserving operator recovery and application-controlled role
provisioning.

Assign revokes all target sessions by default; set
`RevokeSessionsOnRoleGrant` to `false` when every relevant host policy queries
current membership online and a grant does not need to invalidate existing
tickets. For a self-grant with revocation enabled, the confirmed current
session is retained and the actor's other sessions are revoked.

`RevokeSessionsOnRoleRemoval` controls removal separately and defaults to
`HelloSessionRevocationScope.Always`, preserving the secure behavior of earlier
hosts. `ProtectedOnly` revokes all sessions when the role protection resolved
by `HelloAdminRoleRulesEvaluator` is `System` or `Retained`, but preserves them
for `Structural` and unprotected roles. `Never` preserves sessions for every
removable role. The existing self-removal rules are unchanged: an actor still
cannot remove their own `System` or `Retained` role. When removal preserves
sessions, `HelloAdminRoleActionResult.SessionsRevoked` and
`CurrentActorSessionRevoked` are both `false`, so the Razor UI stays on the
users page instead of redirecting the actor to sign-in.

The Admin policies themselves query current membership, so they observe a
removal immediately even when sessions are preserved. Refresh also projects
claims from the current role store, and the removed role is absent from the new
access token. However, role claims already embedded in an issued OAuth access
token remain until that token is refreshed or expires. This residual window is
bounded by `HelloAuthorizationServerOptions.AccessTokenLifetime`, which
defaults to 15 minutes. A host that authorizes from token role claims and cannot
accept that window must keep `RevokeSessionsOnRoleRemoval =
HelloSessionRevocationScope.Always`. Online policies such as
`AddSkopkaHelloCurrentRolePolicy` can safely observe removals without ending
the logical session.

Identity emits the assign/remove security events. Hello emits post-commit
`hello.admin.role.created`, `.updated` and `.deleted` events for role CRUD
through `IHelloSecurityEventSink`, including actor and role ids but no role
description or other free-form input. The ready Server copies them to its
durable audit outbox. An audit sink failure is logged and cannot roll back or
misreport an already committed Identity mutation.

Admin user deletion, like self-service deletion, emits Identity's
`identity.user.deleted` through the same sink. `SubjectUserId` identifies the
deleted account, `ActorUserId` identifies the administrator when request
context is available, and `DeliveryStage` is `AfterIdentityCommit`.
