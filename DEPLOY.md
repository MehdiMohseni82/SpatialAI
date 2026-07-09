# Deploying SpatialAI (public, multi-tenant)

The public build runs one container. Each visitor **registers** (email captured), gets a **fully
isolated space**, and plays under a **hard message budget** on **Claude Haiku 4.5**. Vision/blueprint
import is disabled. State (SQLite + saved spaces + session keys) lives on a mounted volume.

## Quick start

```bash
# 1. Provide your Anthropic key (and optionally SMTP + public URL) in a .env file next to compose:
cat > .env <<'EOF'
ANTHROPIC_API_KEY=sk-ant-...
PUBLIC_BASE_URL=https://spatialai.example.com
# Optional SMTP (needed only if AUTH_REQUIRE_VERIFICATION=true):
SMTP_HOST=smtp.example.com
SMTP_PORT=587
SMTP_USER=apikey
SMTP_PASS=...
SMTP_FROM=SpatialAI <no-reply@example.com>
EOF

# 2. Build + run
docker compose up -d --build

# App is on http://<server>:8080  (put a reverse proxy / TLS in front for a public URL)
```

## Configuration (env vars)

| Var | Default | Meaning |
|---|---|---|
| `ANTHROPIC_API_KEY` | — (required) | Your Anthropic key; the app bills to your account. |
| `LLM__Model` | `claude-haiku-4-5` | Model id. Cheapest tool-capable Claude. |
| `PublicBaseUrl` | `http://localhost:8080` | Origin used to build magic-link URLs in emails. |
| `Budget__MessagesPerUser` | `50` | Per-user message cap. |
| `Budget__GlobalMessageCeiling` | `1500` | Hard total-messages backstop across everyone. |
| `Auth__Required` | `true` | Gate all `/api/*` behind a session (public mode). |
| `Auth__RequireVerification` | `true` | Email magic-link verification. **Set `false`** to sign users in immediately after register (no email) — the hedge if conference email is flaky; still captures the email. |
| `Email__{Host,Port,User,Pass,From}` | — | SMTP for magic links (only when verification is on). |
| `PublicMode` | `true` | Disables the vision/blueprint import token-sink. |

All state persists in the `spatialai-data` volume (`/data`: `catalog.db`, `app.db`, `spaces/`, `keys/`).
It survives `docker compose up --build`. Back it up if you want to keep the captured emails.

## Notes
- **HTTPS:** terminate TLS at a reverse proxy (Caddy/nginx/Traefik) → forward to `:8080`. Session and
  anon cookies auto-set `Secure` when the request is HTTPS. Set `PublicBaseUrl` to the https origin so
  magic links point at the right place.
- **No-email fallback:** `AUTH_REQUIRE_VERIFICATION=false` → registration is one screen → straight into
  the app; you still collect name+email in `app.db`.
- **Reset budgets/scenes:** budgets are in-memory (a restart resets them). To wipe saved spaces + users,
  remove the volume: `docker compose down -v`.
- **Local run without Docker:** `dotnet run --project src/SpatialAI.Api` (listens on :5005); set the same
  keys via `dotnet user-secrets` or env. Leave `Auth:Required` unset for open dev mode.

## Run it locally (workshop attendees)

Make your change, run the app, and see it work — or break — on your own laptop.

1. **Prove the tool with no key** (the fastest "it works" check):
   ```bash
   dotnet run --project src/SpatialAI.Api          # open dev mode, http://localhost:5005
   # in another terminal:
   curl -X POST localhost:5005/api/tools/enclose_room -H content-type:application/json -d '{"roomName":"Yard"}'
   ```
   A 4-segment fence appears in the viewer. No LLM, no API key needed.

2. **Natural-language chat (bring your own key):**
   ```bash
   cp .env.example .env          # paste your ANTHROPIC_API_KEY from console.anthropic.com
   # PowerShell: $env:ANTHROPIC_API_KEY="sk-ant-..."   |   bash: export ANTHROPIC_API_KEY=sk-ant-...
   dotnet run --project src/SpatialAI.Api
   ```
   Type *"make a 12×10 yard and put a fence around it"* in the chat.

3. **Container path (optional):** `cp .env.example .env`, paste your key, then
   `docker compose up --build` → http://localhost:8080. Add `Auth__Required=false` to `.env` for open mode.

**See how it does NOT work** — the two failure signals to watch for:
- **Bad/blank key** → the chat returns an auth error (shown in the app and the server log).
- **Broken tool** (typo in the router `switch` or the tool schema) → the `curl` above returns a 400/500
  or the wrong scene, and `dotnet run --project src/SpatialAI.Bridge -- --list-tools` won't list `enclose_room`.

## One-click deploy to production (CI)

`.github/workflows/deploy.yml` ships to the live server on demand — no manual tar/scp.

- **Trigger:** GitHub → **Actions** tab → **Deploy to production** → **Run workflow**. Manual only;
  nothing deploys on push.
- **What it does:** runs the full test suite → if green, rsyncs the code to `/opt/spatial` (the server
  `.env` and Docker data volume are excluded, so keys and saved spaces are untouched) →
  `docker compose up -d --build` → curls the live site to confirm it answers.
- **A broken change fails the test job and never deploys** — that red pipeline is the point.

**One-time setup** — GitHub → repo → Settings → Secrets and variables → Actions:

| Secret | Value |
|---|---|
| `SSH_PRIVATE_KEY` | contents of the server SSH private key |
| `SSH_HOST` | `152.53.226.44` |
| `SSH_USER` | `root` |

> ⚠️ A redeploy drops all live SSE connections and wipes in-memory scenes. Don't Run workflow during a
> live attendee session unless you mean to. Do your first real run **before** the workshop, not on stage.
