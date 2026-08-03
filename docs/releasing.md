# Releasing

Releases are published from Git tags by `.github/workflows/release.yml`.
The workflow treats all Skopka.Hello libraries as one coordinated release.

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

## Publish a release

Publish and verify the complete Skopka.Identity `0.7.0` package set first.
Hello release jobs restore dependencies from NuGet.org and must not depend on a
developer's local package source.

Start from a verified commit on `main`, then create and push an annotated
semantic-version tag:

```shell
git switch main
git pull --ff-only
git tag -a v0.4.0 -m "Skopka.Hello 0.4.0"
git push origin v0.4.0
```

The workflow removes the leading `v` and uses the remainder as the assembly and
NuGet package version. Tags must use SemVer with no leading zeroes and without
build metadata; stable and prerelease versions are supported. Before any
publication, one job restores, builds, runs the unit and PostgreSQL
Testcontainers integration tests, audits dependencies, packs the complete
solution and verifies the exact five package and symbol package filenames. The
tagged commit must be reachable from `origin/main`.

All `.nupkg` files are then submitted by one NuGet push step. NuGet.org does not
provide a transaction spanning multiple package IDs, so a network failure can
still leave a partially visible release. `--skip-duplicate` makes rerunning the
same GitHub Actions job safe: already accepted packages are skipped and the
remaining packages are submitted.

After the push, the workflow waits until the exact version of all five package
IDs is readable from NuGet.org's public flat-container endpoint. The GitHub
Release is created only after the complete package set is visible.

The generated GitHub Release contains the same `.nupkg` and `.snupkg` files.
Normal branch pushes and pull requests never publish; their package artifacts
are retained by CI for 14 days.

Container image build and publication are intentionally not part of this
release workflow yet. The test phase still requires the Docker Engine available
on GitHub-hosted runners because the integration suite uses PostgreSQL
Testcontainers.
