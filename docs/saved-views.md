# Saved Views: running it

A Saved View is a user's dashboard layout (Metric keys, how each is drawn, where it sits on a 12-column grid). See `docs/adr/0004-saved-views.md` for why it
is built this way and `docs/saved-views-ui-spec.md` for the screens.

## Routes

All under `/api/v1`. The Provider is in the path and checked against the caller's assignments on every request.

| Route | Needs | What |
|---|---|---|
| `GET /providers/{id}/metrics/catalog` | `prism.dashboard.view` | The Metric Catalog: categories, Metrics, presentations, options |
| `GET /providers/{id}/saved-views` | view | My views and the ones shared with me |
| `GET /providers/{id}/saved-views/{viewId}` | view | One view with its layout; `ETag` is the version |
| `POST /providers/{id}/saved-views` | `prism.views.manage` | Create |
| `PUT /providers/{id}/saved-views/{viewId}` | manage, owner | Save; send `If-Match` (412 if stale, 428 if missing) |
| `DELETE /providers/{id}/saved-views/{viewId}` | manage, owner | Delete with its shares and invitations |
| `POST .../{viewId}/invitations` | manage, owner | Email up to 10 invitations |
| `GET .../{viewId}/shares` | manage, owner | Recipients and pending invitations |
| `DELETE .../{viewId}/shares/{shareId}` | manage, owner | Remove a recipient |
| `DELETE .../{viewId}/invitations/{invitationId}` | manage, owner | Cancel an invitation |
| `POST /saved-view-invitations/redeem` | view | Use a token from an emailed link |

## Limits

24 widgets per view, 12 columns, 200 rows, 32 KB of layout, 64 KB request body, 80-character name, 300-character description, 100 views per owner and Provider,
50 recipients and pending invitations per view, 10 addresses per request, 30 invitations per owner per hour, one invitation per address per view (a new one replaces it).

## Turning on invitations

Invitations are off. To turn them on for a host, put this in that host's `appsettings.<Environment>.json` (never the base file):

```json
{
  "SavedViews": {
    "Invitations": {
      "Enabled": true,
      "ExpiryDays": 7,
      "PublicBaseUrl": "https://prism.example.org",
      "SmtpHost": "email-smtp.us-east-1.amazonaws.com",
      "SmtpPort": 587,
      "FromAddress": "prism@example.org",
      "FromName": "Prism"
    }
  }
}
```

`SmtpUserName` and `SmtpPassword` are secrets: supply them through AWS Secrets Manager like the other secrets. Start-up fails if invitations are enabled and the
public address is not https (outside Development), the host or sender is missing, or the expiry is outside 1 to 30 days. The link in the email is built from
`PublicBaseUrl`, never from a request. TLS to the mail server is always on.

Until a host enables them, `POST .../invitations` answers 503.

## Redeeming

The email links to `/saved-view-invitations/redeem?token=...`. The SPA signs the user in if needed (the return URL goes through the usual same-origin check), removes
the token from the address bar, and calls the redeem route. A token works once, until it expires, and not after the owner cancels it. The user must be assigned to
the view's Provider. Every failure is the same answer, so nothing reveals whether a token, view or address exists. The portal session carries no email claim, so
the invited address is not matched to the person who opens the link: the link is the credential. The owner's share list shows who redeemed it and can remove them.

## Pushed View Templates

Internal Users can push a View Template to Providers (ADR 0009, `openspec/changes/view-templates`). A push keeps a snapshot of the template. Each user receives their own copy, owned by them and marked as created by "Prism team", the first time they open their Saved Views in an audience Provider after the push. That happens in `GET /api/v1/providers/{providerId}/saved-views`, before the list is built. The delivery is once per push, user and Provider, bounded to 10 copies per request, and a copy the user deletes is not delivered again. The copy's Provider and owner come from the route and the session, never from the push. Stopping a push stops future deliveries and never removes a copy. A name the user already has gets a number added, and their own view is not touched. Admin routes are under `/api/v1/admin/view-templates` and `/api/v1/admin/template-assignments`.

## Not built yet

- Per-viewer Label overrides (the Reporting Division wording still comes from the Service Line defaults).
- Reporting Division filtering in any Metric query, and the planned Metrics (AR days, denials, negotiations, fleet detail).
- A scheduled purge of old invitations. Unused ones that expired or were revoked more than 30 days ago are removed whenever the owner sends a new invitation.
