# 14 — CI: build and test every pull request

**What to build:** A pull request into `main` says for itself whether it builds and passes, instead of resting on whoever opened it having run the suites locally.

**Status:** ready-for-human

Found while opening the PR for ticket 11: the repo has no `.github/workflows`
and GitHub reports zero workflows, so `gh pr checks` answers "no checks
reported" on every branch. Thirteen tickets have merged on local runs alone.

- [x] Runs on every pull request into `main`, and on pushes to `main`.
- [x] Backend job: `dotnet restore` / `build` / `test` over `FlashFlights.slnx`.
- [x] The Ordering suite runs for real rather than being skipped — its
      Testcontainers PostgreSQL fixture needs a Docker daemon, which
      `ubuntu-latest` has. A green run that skipped the concurrency proof would
      be worse than no run.
- [x] SPA job: `npm ci`, `lint`, `test`, `build` under `web/`.
- [x] The two are separate jobs, so a red SPA does not hide the backend's state.
- [x] Superseded runs on a branch are cancelled; runs on `main` are not.

## Comments

### Deliberately not in scope

**Building the Docker images.** `docker compose up` is the repo's headline
promise and nothing verifies the four Dockerfiles still build — but doing it
would roughly triple CI time, and it is really ticket 12's territory ("clone and
run"). Worth its own ticket if the images ever break silently.

**Caching NuGet.** `setup-node` caches npm here; the .NET side restores cold
every run. Worth adding if the backend job gets slow, not before.
