# Deploying to Staging

Staging is the only environment. A push to `main` runs CI; when CI passes, `.github/workflows/deploy.yml` builds the image once and (when enabled) migrates the database. You can also start it by hand from the Actions tab, but only on `main`.

```
push to main -> CI passes -> preflight -> build and push image -> migrate (when enabled) -> deploy (placeholder)
```

## What it does today

- **Preflight** checks the Staging secrets and variables exist, the AWS credentials work and the ECR repository exists. It runs before anything is built or changed.
- **Build and push** builds `docker/Dockerfile` once per commit and pushes it to ECR tagged `sha-<full commit>`. A tag is never overwritten; a redeploy of the same commit reuses the image.
- **Migrate** builds an EF Core migrations bundle and applies it to the Prism database. It stays off until `STAGE_MIGRATIONS_ENABLED` is `true`.
- **Deploy** is a placeholder that prints the image reference. Nothing is rolled out yet.

One deploy runs at a time and a running deploy is never cancelled: a cancelled run could leave the database migrated while the old image still serves. A newer push waits.

## What DevOps needs to do

1. **ECR repository** for Prism, with immutable tags. Set it as `ECR_REPOSITORY`.
2. **GitHub environment `Staging`** (Settings, Environments), limited to the `main` branch, holding:
   - Secrets `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` for an IAM user that can push to that repository, describe it, and read the one database secret below. The key is long-lived. Moving to a GitHub OIDC role later only changes the three `configure-aws-credentials` steps.
   - Variables: `ECR_REPOSITORY` (required), `AWS_REGION` (default `us-east-1`), `DB_SECRET_ID`, `STAGE_MIGRATIONS_ENABLED`, `STAGE_MIGRATION_RUNNER`.
3. **Package access.** The SPA install reads the `@ems-management-consultants` packages with the workflow's token, as CI does, so this repository needs read access on those packages.
4. **Migrations.** Create a Secrets Manager secret whose value is the plain Prism connection string and set `DB_SECRET_ID` to its id. The Staging database is private, so set `STAGE_MIGRATION_RUNNER` to a runner label that can reach it (GitHub-hosted runners cannot). Then set `STAGE_MIGRATIONS_ENABLED` to `true`. Migrations run before the new image starts, so each one must keep working with the previous version of the app: additive and forward-only, no drops or renames in the same release.
5. **Deploy step.** Replace the `deploy` job's placeholder step with whatever rolls Staging onto `needs.build-push.outputs.image`. Keep it inside the `Staging` environment.
6. **Runtime settings.** The container reads its configuration and secrets from AWS Secrets Manager and the App Config sidecar when turned on. Both ship off. See [secrets.md](secrets.md) and [feature-flags.md](feature-flags.md).

## Rollback

Deploy the previous commit's image: its `sha-<commit>` tag still exists in ECR. Migrations are not reverted, which is safe because they are additive.

## Why it is smaller than M6's pipeline

M6 runs four environments and six images, promotes by digest, and gates Production on an approval. Prism has one environment and one image, so those parts are left out. What is kept: build once with an immutable tag, deploy only a green `main`, check everything possible before changing anything, never cancel a deploy in flight, pinned actions and credentials held by the Staging environment.
