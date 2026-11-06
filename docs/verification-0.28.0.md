# Local verification: Hello 0.28.0 / Identity 0.17.0

Verified locally on Windows with Docker Desktop and Chromium on 2026-11-06.
No push, release tag, package publication or application deployment was performed.

| Check | Result |
| --- | --- |
| Identity restore and Release build | Passed, no build warnings |
| Identity full test suite | 360 passed, including SQLite and real PostgreSQL |
| Identity dependency audit | No known vulnerable packages reported |
| Identity package set and standalone consumer | Seven packages and seven symbol packages verified; consumer ran |
| Hello restore through `NuGet.Config` and Release build | Passed, no build warnings |
| Hello unit tests | 524 passed |
| Hello PostgreSQL integration tests | 84 passed |
| Playwright Chromium install and browser tests | 10 passed |
| Administrative reset audit assertion | Passed in a subsequent focused integration run |
| Hello formatting and dependency audit | Passed; no known vulnerable packages reported |
| Hello package set and standalone consumer | Six packages and six symbol packages verified; consumer ran |
| Upgrade-guide role-policy example | Compiled against the candidate packages |
| Server container | Built with the local Identity candidate source described below |
| Products documentation baseline | Docker build passed with the existing 24 warnings; stable pins unchanged |

The solution-wide pack command reports that executable/sample/test projects are
not packable; the exact package-set checks confirm only the intended libraries
were produced. These messages are not missing library artifacts.

## Unpublished dependency

Identity 0.17.0 was packed into
`H:/Rep/skopka/identity/artifacts/packages-0.17.0`, then restored locally before
Hello verification. No developer-specific source was added to the committed
`NuGet.Config`. A clean machine cannot restore this candidate from NuGet.org
until Identity is released or supplied as a local package source.

The Server build used a temporary copy of the repository Dockerfile at
`artifacts/Dockerfile.verify028`. The only changes were a named build context
containing those Identity packages and an additional restore source in the build
stage. The production Dockerfile and runtime stage were unchanged:

```powershell
docker build -f artifacts/Dockerfile.verify028 `
  --build-context identitypackages=H:/Rep/skopka/identity/artifacts/packages-0.17.0 `
  -t skopka-hello:0.28.0-local .
```

This verifies compilation and packaging of the Server against the unpublished
dependency. The stock public-feed Docker command must be run again after the
Identity release; this report does not claim a public-feed restore of 0.17.0.

## Coverage and limits

Tests cover required-factor admission and unchanged defaults, enrollment before
session issuance, recovery-code login, malformed/expired/stale/replayed
continuations, concurrent completion, live policy changes, phone/passkey/external
sign-in, native/OAuth assurance propagation, phone ownership disclosure after
proof, session binding, custom field validation, administrative step-up/reset and
audit, and the existing client-administration PRG regression.

Generic cross-device approval does not attest the factor in Identity and therefore
fails closed when the host's new mandatory rule applies. Offline JWT consumers
observe later policy changes only at token expiry. See the
[second-factor guide](mandatory-second-factor.md).

RU/EN Products drafts are prepared under
`H:/Rep/skopka/products/docs/release-drafts/identity-0.17-hello-0.28/` outside the
stable DocFX content globs. The site build still uses the released source pins;
candidate API generation and site publication await the separately authorized
releases. Existing Products warnings concern duplicate UIDs/migration members
and the pinned Abstraction SourceLink dependency, not Hello's audited packages.
