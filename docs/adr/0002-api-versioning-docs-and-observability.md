# 2. URL versioning by route group, signed-in API docs, Serilog and OpenTelemetry

Date: 2026-11-21

## Status

Accepted

## Context

The JSON API may be shown to customers, so its contract has to be stable, documented and hard to change by accident. The service also needs
logs and traces that are useful in production without ever carrying personal data or credentials.

## Decision

- **Version in the URL, by route group.** The JSON API lives under `/api/v1` (`ApiRoutes.V1`). A breaking change adds `/api/v2` beside it.
  We did not add the `Asp.Versioning` packages: with one version they add a dependency and no behaviour, and the built-in OpenAPI document
  already carries the version. Revisit when `v2` exists and two documents are needed.
- **The sign-in handshake is not versioned.** `/api/auth/sso/*` is a browser flow whose callback URL is registered in the Account Portal, so it cannot move.
- **A committed snapshot of the OpenAPI document** (`tests/Prism.Tests.Integration/Snapshots/openapi.v1.json`) fails the build on any change until a
  person updates it on purpose. Every operation must have a summary and a description.
- **The API description (`/openapi/v1.json`) and the reference page (`/docs`, Scalar) require a signed-in user in every environment.**
- **Serilog for logs.** JSON lines (compact) in deployed environments, a readable line locally. The request log carries the path and never the query string,
  and the framework's own request logging, which does include it, is set to Warning. Every line carries a correlation id.
- **OpenTelemetry for traces and metrics.** Exported only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Span tags are stripped of query strings, because a
  query can hold a login token or a nonce.

## Consequences

- Moving `/api/me` to `/api/v1/me` was the one breaking change, made before any client existed.
- Tests assert that a login token and its nonce appear in no log line and no span tag.
- Adding a data route means putting it in the `v1` group and running the snapshot update, then reading the diff.
