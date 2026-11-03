# Enabling OAuth client administration in SkopiClub.Auth

The SkopiClub.Auth source inspected for these instructions is
`H:/Rep/skopi-club/hello/src/SkopiClub.Auth/Program.cs`. Its existing host
already registers the authorization server, Admin, UI, role services, OTP
verification and delivery. **Do not add a second registration or store.** The
feature is included in Hello 0.25.0; these instructions describe the
upgrade after the coordinated release is available, not a change made to the
SkopiClub repository.

1. Update `H:/Rep/skopi-club/hello/Directory.Packages.props`: its five referenced
   `Skopka.Hello*` packages are currently pinned to `0.22.1`. Set all of them to
   `0.25.1`. Align its four
   direct `Skopka.Identity*` references with the new Hello package's dependency
   requirements; this Hello checkout currently uses Identity `0.16.0`, while
   SkopiClub pins `0.14.0`. Check transitive dependency resolution and run the
   SkopiClub test suite for upgrade compatibility. Its `NuGet.Config` uses
   `vendor/packages` and NuGet.org: make the coordinated packages available to
   the deployment's chosen feed and update its vendored artifacts and checksums
   if that feed remains in use. Verify that all six 0.25.1 packages are available
   before starting the host upgrade. Custom UI localization dictionaries must place
   translation keys under `texts`; unknown or duplicate root properties are rejected
   at startup.
2. Keep the existing `AddAuthorizationStorage` using
   `SkopiClubAuthorizationDbContext`,
   `AddSkopkaHelloAuthorizationServer<SkopiClubProfile>`, and
   `AddSkopkaHelloAdmin<SkopiClubProfile, SkopiClubAdminProfileProjector>`.
   `Program.cs` already maps `MapSkopkaHelloAuthorizationServer`,
   `MapSkopkaHelloAdmin`, and `MapSkopkaHelloUi` after authentication and
   authorization. The Admin feature is discovered from the registered client
   management service. Existing SkopiClub UserInfo and local validation
   configuration should remain in place and be tested with the new packages.
3. In `SkopiClub:AuthorizationServer`, keep the exact existing configured
   `Clients` and production secrets. `Resource` currently defaults to
   `skopi-club-api` and `AdditionalScopes` includes `mail`. Add only intended
   additional audiences to `AdditionalResources`, for example
   `SkopiClub__AuthorizationServer__AdditionalResources__0=stalwart` if it is
   not already available from a configured client. Configure an identical
   catalog on each replica and restart them together. Client scopes and
   audiences can only be selected from this catalog.
4. Retain the existing `SkopiClub:Admin` read and delete policies
   (`SkopiClub.Admin.Read` and `SkopiClub.Admin.Delete`), both backed by the
   `SkopiClub.Admin` role in the checked-in settings. Ensure the administrator
   has an online session and a usable confirmed-contact verification channel,
   or an enabled authenticator when required. The checked-in delivery defaults
   select email but leave the provider ID empty; production must configure the
   actual provider and SMTP settings before email OTP can be delivered.
5. Back up the authorization database, deploy the new packages, and run the
   existing one-shot `SkopiClub.Auth --migrate` command before starting upgraded
   replicas. Its `MigrateAsync` migrates Identity and
   `SkopiClubAuthorizationDbContext`, then calls
   `IHelloAuthorizationClientSynchronizer.SynchronizeAsync`. Do not run this
   command concurrently across replicas. Existing configured client IDs are
   adopted from untagged legacy registrations; unmatched legacy and
   administrator-owned clients are preserved. A configuration collision with an
   administrator-owned ID fails before client synchronization writes. Old codes
   and refresh grants without client revision metadata require a fresh sign-in.

With the default UI prefix, open `/hello/admin/clients`. Create a registration,
complete the OTP challenge, and copy the displayed confidential secret into the
relying application's secret manager immediately. Client IDs are immutable;
public clients have no secret. Set the relying application to authorization code
with PKCE S256 and its exact registered redirect. Editing or rotating a client
invalidates its existing grants; rotation replaces the old secret. An operator
can continue to maintain configuration-owned clients through configuration and
`--migrate`; those registrations are read-only in the Admin page.

See [authorization server](authorization-server.md) for API shapes, ownership,
policy, catalog, and secret handling. No SkopiClub source or deployment was
modified as part of these instructions.
