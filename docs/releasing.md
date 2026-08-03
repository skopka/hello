# Releasing

Releases are published from Git tags by `.github/workflows/release.yml`.
The workflow treats all Skopka.Hello libraries and the ready Server container
as one coordinated release.

## Repository setup

Create a NuGet.org API key that can publish every package listed below and store
it in the GitHub repository Actions secret named `NUGET_API_KEY`:

- `Skopka.Hello`
- `Skopka.Hello.Admin`
- `Skopka.Hello.Endpoints`
- `Skopka.Hello.Oidc`
- `Skopka.Hello.UI`

The same semantic version is assigned to all five packages. Do not publish an
individual project or create package-specific release jobs.

The workflow publishes the server image to GitHub Container Registry using the
repository's `GITHUB_TOKEN`. In repository package settings, keep Actions write
access enabled for `ghcr.io/skopka/hello`.

## Publish a release

Publish and verify the complete Skopka.Identity version declared in
`Directory.Packages.props` first. Hello release jobs restore dependencies from
NuGet.org and must not depend on a developer's local package source.

Start from a verified commit on `main`, then create and push an annotated
semantic-version tag:

```shell
git switch main
git pull --ff-only
git tag -a v0.5.0 -m "Skopka.Hello 0.5.0"
git push origin v0.5.0
```

The workflow removes the leading `v` and uses the remainder as the assembly and
NuGet package version. The tag's base version must equal `VersionPrefix` in
`Directory.Build.props`. Tags must use SemVer with no leading zeroes and
without build metadata; stable and prerelease versions are supported. Before
any publication, one job restores, verifies formatting, builds, runs the unit
and PostgreSQL Testcontainers integration tests, audits dependencies, packs
the complete solution and verifies the exact five package and symbol package
filenames. The tagged commit must be reachable from `origin/main`.
Third-party Actions are pinned to reviewed commit SHAs; Dependabot proposes
updates to those pins.

Before the first immutable write, the NuGet job proves that none of the five
package IDs already has that version. It then submits the packages in dependency
order without `--skip-duplicate`. This prevents an unrelated or stale package
from being silently accepted as part of the coordinated release. NuGet.org does
not provide a transaction spanning multiple package IDs, so a network failure
can still leave a partially visible release. Never reuse such a version: fix the
cause, increment the patch version and create a new tag.

After the push, the workflow waits until the exact version of all five package
IDs is readable from NuGet.org's public flat-container endpoint. A separate
dependent job then builds and publishes `ghcr.io/skopka/hello:<version>` and a
commit-SHA tag from the same source. If that job fails, use GitHub Actions
**Re-run failed jobs** so the successful immutable NuGet job is not repeated.
Stable tags also update `latest`; prereleases do not. The image
contains SBOM and maximum provenance attestations. The GitHub Release is
created only after both the complete package set and the exact container tag
are visible.

The generated GitHub Release contains the same `.nupkg` and `.snupkg` files.
Normal branch pushes and pull requests never publish; their package artifacts
are retained by CI for 14 days, and CI builds the Server image without pushing
it. Both CI and the release integration suite require Docker Engine for
PostgreSQL Testcontainers and the container build.
