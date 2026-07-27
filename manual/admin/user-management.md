# User Management

The **Users** screen manages who can access Proxytrace, what they can do, and which projects they
belong to. It lives in the admin-only **Settings** hub — open **Settings** from the sidebar, then
choose **Users** under the *Workspace* group (direct link: `/settings/users`). The whole Settings
area is visible only to users with the **Admin** role, and the underlying `/api/users` endpoints
enforce the same restriction — client-side gating is convenience only.

## Roles

Every user holds exactly one role:

| Role | Capabilities |
| --- | --- |
| **Member** | Standard read/write access to project data. |
| **Admin** | Everything a Member can do, plus user management (this page). |

Change a user's role with the inline **Role** dropdown on their row — this promotes or demotes
them immediately.

::: warning The last Admin is protected
You cannot demote or delete the **last remaining Admin**, and you cannot change or delete **your
own** account from this page. Those actions are disabled in the UI and rejected by the API
(HTTP 409), so the instance can never be locked out of administration.
:::

::: warning A user who owns API keys cannot be deleted outright
Every API key records the person who created it. Deleting a user who still owns keys is **refused**,
and the message lists the keys by name.

This is deliberate: an API key is stored in a form that cannot be read back, so a deleted key cannot
be restored — only replaced — and anything configured with it stops working immediately, with no
indication beyond rejected requests. Before deleting the user, go to **Settings → Providers** and
either delete the keys (updating whatever uses them) or reissue equivalents under another owner.
:::

## Adding users (local mode)

When Proxytrace uses local username/password authentication, users are onboarded by invitation —
there is no self-service sign-up:

1. Under **Invite a user**, enter the person's **email**, pick their initial **role**, and click
   **Create invite**.
2. Proxytrace generates a single-use invite link (valid for 7 days), **shown once right after you
   create the invite**. **Copy the link then and share it yourself** — Proxytrace does not send
   email.
3. The recipient opens the link, sets a password, and is signed in. They now appear in the
   **All users** list.

Outstanding invitations are listed under **Pending invites** with their status (Pending, Used, or
Expired). Use **Revoke** to cancel one that hasn't been used yet. The link is **not** shown again
after creation — invite tokens are stored hashed, so if you lose a link, revoke the invite and create
a new one.

::: warning Free tier is single-user
The **Free** tier is limited to a **single user**. Once that seat is taken, creating an invite is
rejected with an upgrade prompt — user management is effectively disabled until you upgrade. See
[Licensing](/admin/licensing).
:::

## Assigning users to projects

Click **Projects** on a user's row to open the project assignment editor. Tick a project to add
the user as a member; untick to remove them. The same membership can also be managed from the
project side under **Settings → Projects**.

## Resetting a password

Users who forget their password use **Forgot password?** on the sign-in screen to request a reset
link themselves. How that link reaches them depends on whether outgoing email is configured:

- **Email configured** ([SMTP set up](/admin/email)) — Proxytrace emails the one-time reset link
  directly to the user. The link is valid for **1 hour** and can be used once.
- **No email configured (or the email send fails)** — the link can't be delivered. Proxytrace writes a
  **redacted** warning to the server log: it records *that* a reset was requested, a short
  non-reversible token reference, and the expiry, but **not** the actual link (so anyone who can read
  your logs can't hijack the account). Prefer the **Reset password** button below, which never touches
  the log.

You can reset on a user's behalf without involving email at all: on the user's row click **Reset
password**. Proxytrace mints a one-time link and shows it once in a dialog — copy it and share it with
the user over a trusted channel. As with invites, the link is stored hashed and **never shown again**.

::: warning Recovering a locked-out sole Admin with no email
If you are the **only** Admin, you are locked out, **and** SMTP is not configured, the **Reset
password** button isn't reachable (it's behind admin sign-in). For this last-resort case only, set
`Authentication:EmergencyLogResetLink` to `true` in configuration and restart. Proxytrace will then
write the **full** one-time reset link to the server log on the next **Forgot password?** request —
retrieve it from the log, complete the reset, then set the flag back to `false`. While it is on,
anyone who can read the server log within the link's 1-hour lifetime can take over the account, so
keep it off in normal operation. See [Configuration](/admin/configuration).
:::

::: tip Resets don't sign existing sessions out
A new password takes effect immediately for new sign-ins, but any session the user already has open
stays valid until it expires. To revoke access right away, delete the user.
:::

External (SSO) users have no Proxytrace password, so **Reset password** is disabled for them — manage
their credentials at your identity provider.

## Resetting two-factor authentication

Users turn [two-factor authentication](/guide/two-factor-authentication) on and off themselves under
**Account security**, and they get one-time backup codes for when they lose their device. If a user
loses **both** their authenticator app and their backup codes, they're locked out — and only an admin
can recover them.

On the user's row, click **Reset MFA** (shown only for users who currently have it enabled) and
confirm. This turns off two-factor authentication for that user and discards their backup codes; they
can then sign in with just their password and set it up again. The reset is recorded in the
[audit log](/admin/audit-log).

## How this works with OIDC (SSO)

When Proxytrace is configured against an external **OIDC** identity provider, account creation is
delegated to that provider:

- **No invitations or passwords.** The invite form and pending-invites list are hidden, and the
  invite/sign-up/login endpoints are disabled — your IdP owns authentication.
- **Just-in-time provisioning.** A user record is created automatically the first time someone
  signs in through the IdP. The **very first** user to sign in becomes an **Admin**; everyone
  after that starts as a **Member**.
- **Roles still live in Proxytrace.** Promotions and demotions you make here are persisted and
  survive subsequent logins — the IdP does not overwrite them.
- **Removal is local.** Deleting an SSO user removes their Proxytrace record (and project
  memberships), but does **not** disable them at the identity provider. If their IdP account is
  still active, they will be re-provisioned as a **Member** the next time they sign in. To remove
  someone permanently, deactivate them at the IdP (optionally also deleting the local record to
  revoke their current role and project access right away).

The **Sign-in** column shows each user's source — **Local** for password accounts, **SSO** for
OIDC-provisioned ones.

See [Configuration](/admin/configuration) for how to switch between local and OIDC authentication.
