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
    options.ProtectedRoleNames = ["iq-author", "iq-teacher"];
    options.RoleManagementEnabled = false;
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
2. Every handler explicitly evaluates a read, manage or delete policy. The
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
      "ReadRoleName": "Skopka.Hello.Admin",
      "ManageRoleName": "Skopka.Hello.Admin",
      "DeleteRoleName": "Skopka.Hello.Admin",
      "ProtectedRoleNames": [],
      "RoleManagementEnabled": true
    }
  }
}
```

Use different role names when read, state-management and deletion privileges
must be separated. Policy names must be distinct. The Razor route is composed
from the Hello UI prefix plus the admin API prefix, so the defaults expose API
under `/admin` and UI under `/hello/admin/users` and `/hello/admin/roles`.
The protected `/hello/admin` entry route redirects to the user list.
Every role mutation, including membership assignment and removal, requires the
highest `DeletePolicyName`. This prevents an administrator limited to ordinary
user management from granting themselves a higher authorization role.
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

Roles named by `ReadRoleName`, `ManageRoleName`, `DeleteRoleName` or
`ProtectedRoleNames` cannot be renamed or deleted through this surface. Name
matching trims configured values and ignores case. An administrator also
cannot remove their own protected role. Assigning protected roles and removing
them from other users remain available.

`ProtectedRoleNames` is empty by default. Set `RoleManagementEnabled` to
`false` when the host owns the complete role catalog in code. Role creation,
update and deletion are then rejected by both API and Razor handlers, and their
Razor forms are hidden. Role queries and user membership assignment/removal
remain available; `RoleManagementEnabled` defaults to `true`.

Removing another administrator's last membership is an explicit
high-privilege operation; if operators lock out every administrator, recover
with the bootstrap command.

Assign and remove revoke all target refresh sessions after the membership
change. The Admin policies themselves always query current membership, so the
change affects this module immediately. A host policy based only on JWT role
claims can continue accepting an already-issued stateless access token until
expiry; enable online session validation where immediate revocation is
required.

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
