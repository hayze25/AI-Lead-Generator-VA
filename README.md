# AI Business Assistant

An assistant for small businesses that handles incoming customer inquiries: it **triages** each message with an LLM, **turns sales inquiries into leads**, **drafts replies** for a human to approve, and **sends follow-ups automatically** through n8n.

**Stack:** ASP.NET Core (.NET 10) · EF Core + SQL Server · React (Vite) · n8n · LLM API (Anthropic Messages API by default)

```
 Website form / Facebook / email          ┌───────────────────────────────┐
        │                                 │            n8n                │
        ▼                                 │  1 Send email (webhook)       │
  n8n "Website form" webhook ───────────► │  2 Due follow-ups (every 15m) │
        │  POST /api/automation/inquiries │  3 Website form -> inquiry    │
        ▼                                 └──────▲──────────────┬─────────┘
 ┌──────────────────────────┐   send-email webhook│              │ SMTP
 │  ASP.NET Core API        │─────────────────────┘              ▼
 │  intake -> AI triage ->  │◄── GET due follow-ups ──── customer's inbox
 │  lead -> follow-up       │
 └───────┬───────────┬──────┘
         │ EF Core   │ HTTPS
         ▼           ▼
   SQL Server     LLM API          React dashboard ──► REST API
```

## What it does

1. **Intake.** An inquiry arrives from the dashboard form or from n8n (website form webhook).
2. **AI triage.** The LLM returns structured JSON: category (Sales / Support / Billing / Partnership / Spam / Other), sentiment, urgency, a one-line summary, and a lead score.
3. **Lead pipeline.** Sales and partnership inquiries create (or update) a lead, de-duplicated by email, and schedule a follow-up two days out. Spam is closed automatically.
4. **Draft replies.** One click drafts a reply using your business profile (what you do, tone, FAQ/prices). You can add guidance ("offer a call on Thursday"), edit the draft, and send. **Nothing is sent without a human pressing Send.**
5. **Automated follow-ups.** n8n polls the API every 15 minutes, emails the follow-ups that are due, then reports back so they're marked sent. Marking a lead Won/Lost cancels its pending follow-ups.

## Quick start

Requires Docker, and Node 20+ for the dashboard.

```bash
cp .env.example .env            # then edit it: set AUTH_PASSWORD and AUTH_JWT_KEY (required),
                                # and optionally LLM_API_KEY
docker compose up --build       # SQL Server + API (:5080) + n8n (:5678)

cd frontend
npm install
npm run dev                     # dashboard at http://localhost:5173
```

Sign in as the admin with the username and password you put in `.env` (`AUTH_USERNAME`, default `admin`, and `AUTH_PASSWORD`), or click **Create an account** to register your own business (see *Accounts and registration* below). Generate a key for `AUTH_JWT_KEY` with any password manager, or in PowerShell: `-join ((1..48) | % { [char[]]'abcdefghijklmnopqrstuvwxyz0123456789' | Get-Random })`.

The API creates its own database and tables on first start. With **no `LLM_API_KEY` the app runs in demo mode** (a "Demo mode" badge shows in the header): triage uses simple keyword rules and drafts are templates. Everything else works, so you can try the whole flow without an account. Add a key to use a real model.

### Running the API without Docker

```bash
docker compose up sqlserver -d
cd backend/BizAssistant.Api
dotnet run                      # http://localhost:5080, Development profile
```

In the Development profile the login is `admin` / `admin12345` (set in `appsettings.Development.json`, for local use only; never run the Development profile on a public server). `N8n__DryRun` is also `true`: "sent" emails are logged to the console instead of being posted to n8n, so you can work without n8n running. Docker/Production uses `false`.

## Configuration

Set via `.env` (Docker) or environment variables / `appsettings.json`. Nested keys use `__`.

| Setting | Purpose | Default |
|---|---|---|
| `ConnectionStrings__Default` | SQL Server connection | local Docker SQL Server |
| `Auth__Username` | Login name of the admin, who owns the original business | none (required) |
| `Auth__Password` | Admin password, 8+ characters | none (required) |
| `Auth__AllowRegistration` | Let visitors create their own account and business. `false` turns the Create-account link and endpoint off | `true` |
| `Auth__JwtKey` | Secret used to sign login tokens, 32+ random characters | none (required) |
| `Llm__ApiKey` | LLM API key. Empty = demo mode | empty |
| `Llm__Model` | Model name | `claude-sonnet-5-5` |
| `Llm__BaseUrl` | LLM endpoint | `https://api.anthropic.com` |
| `Automation__ApiKey` | Shared secret n8n sends as `X-Api-Key` | `change-me-api-key` |
| `N8n__BaseUrl` | Where the API reaches n8n | `http://localhost:5678` (`http://n8n:5678` in Docker) |
| `N8n__DryRun` | Log emails instead of calling n8n | `true` in Development only |
| `Database__AutoCreate` | Create schema on startup | `true` |
| `Cors__Origins__0` | Allowed dashboard origin | `http://localhost:5173` |

**Managing the schema yourself:** set `Database__AutoCreate=false` and run `database/schema.sql` instead. It is the same schema. Use one or the other on a given database, not both.

**Changing LLM provider:** the app only talks to the `ILlmService` interface (`Services/LlmService.cs`). Swap `CompleteAsync` or add another implementation. Nothing else knows which model is behind it.

## n8n setup (one-time, ~5 minutes)

1. Open http://localhost:5678 and create the owner account.
2. **Workflows → Import from file**, and import the three files in `n8n/`.
3. In workflows **1** and **2**, open the *Send email* node, create an **SMTP credential**, and set the *From email*.
4. If you changed `AUTOMATION_API_KEY`, update the `X-Api-Key` header in the HTTP Request nodes of workflows **2** and **3** (for production, move it into an n8n Header Auth credential).
5. **Activate** all three workflows (toggle at the top right). Production webhook URLs (`/webhook/...`) only work while a workflow is active.

| Workflow | Trigger | Does |
|---|---|---|
| 1 Send email | Webhook `POST /webhook/send-email` | Sends the email the API hands it. It answers only after the email node finishes, so the API marks a reply *Sent* only if the email really went out. |
| 2 Send due follow-ups | Every 15 minutes | `GET /api/automation/followups/due` → send each → `POST .../{id}/sent` |
| 3 Website form → inquiry | Webhook `POST /webhook/website-lead` | Maps `{name, email, subject, message}` to a new inquiry |

Try the intake:

```bash
curl -X POST http://localhost:5678/webhook/website-lead \
  -H "Content-Type: application/json" \
  -d '{"name":"Ana Reyes","email":"ana@example.com","subject":"Pricing","message":"How much for 20 units? Need them by Friday."}'
```

## API

Dashboard endpoints require a login token (`Authorization: Bearer ...` from `POST /api/auth/login`). Only `/api/auth/login`, `/api/branding` and `/health` are public.

| Method | Route | |
|---|---|---|
| GET | `/api/stats` | Counts, pipeline, last 7 days, `llmMode` |
| GET | `/api/inquiries?status=&category=` | List |
| GET | `/api/inquiries/{id}` | Detail with replies and lead |
| POST | `/api/inquiries` | Create + triage |
| POST | `/api/inquiries/{id}/triage` | Re-run triage |
| POST | `/api/inquiries/{id}/replies/draft` | AI draft, body `{instruction?}` |
| PATCH | `/api/inquiries/{id}/status` | e.g. close |
| PUT | `/api/replies/{id}` | Edit a draft |
| POST | `/api/replies/{id}/send` | Send via n8n |
| GET | `/api/leads?stage=` · `/api/leads/{id}` | Pipeline |
| PATCH | `/api/leads/{id}` | Stage / notes |
| POST | `/api/leads/{id}/followups/draft` | Schedule an AI follow-up, body `{inDays?}` |
| POST | `/api/leads/{id}/followups/{fid}/cancel` | Cancel a pending follow-up |
| GET/PUT | `/api/settings` | Business profile, including name and logo |
| POST | `/api/auth/login` | Body `{username, password}` returns `{token, expiresAt}` |
| GET | `/api/branding` | Public: business name and logo (used by the sign-in page) |

n8n endpoints (require header `X-Api-Key`): `GET /api/automation/followups/due`, `POST /api/automation/followups/{id}/sent`, `POST /api/automation/inquiries`.

## Accounts and registration

- **Admin.** The username and password from `.env` sign in as the admin, who owns the original business (the first one created, lowest id). Data from before registration existed belongs to it automatically; the API upgrades older databases on startup.
- **Register.** *Create an account* on the sign-in page takes a business name, an email and a password (8+ characters). It creates a brand-new, empty business plus the owner's login and signs them in. Each business has its own inquiries, leads, replies, follow-ups, profile, logo and name. The separation is enforced in the data layer: every query is automatically limited to the signed-in business, and the business id comes from the signed login token, so it can't be changed from the browser. The same person can be a lead for two different businesses.
- **Turn it off.** Set `AUTH_ALLOW_REGISTRATION=false` (or `Auth__AllowRegistration=false`) to hide the link and refuse sign-ups. Existing accounts keep working.
- **n8n and several businesses.** Follow-up emails already work across all businesses (each email names its own business). The website-form intake (workflow 3) delivers to the original business; to send a form's inquiries to another business, add `?businessId=<its id>` to the API URL in that workflow's HTTP node. The id is the business's row in `BusinessProfiles`.

## Branding

**Settings → Business profile** lets you upload a logo and set the business name. They show in the header, on the sign-in page and in the browser tab (the logo becomes the tab icon). The logo is shrunk in the browser to at most 256 px and stored in the database (PNG, JPEG or WebP; SVG is refused on purpose, since it can carry scripts). The business name is also what the AI uses when it writes as your business.

## Design decisions

- **The customer's message is never lost.** The inquiry is saved *before* the AI is called. If the LLM is down, it stays `New` and shows a "Run triage" button.
- **Human in the loop for replies.** The AI drafts; a person reviews and sends.
- **Honest status.** A reply is marked Sent only after n8n accepted the email. An unreachable n8n returns a clear 502 instead of a false "sent".
- **Prompt-injection aware.** Customer text is wrapped in tags and the system prompt tells the model to treat it as data, never instructions. The model can only produce text that a human reviews (replies) or a structured triage result that's validated and clamped (unknown enum values fall back to safe defaults).
- **Shared pipeline.** The dashboard and the n8n intake call the same `InquiryService`, so both behave identically.
- **Plain, boring data model.** Five tables, string-backed enums with `CHECK` constraints, unique lead email, indexes on the hot queries (`Status+ReceivedAt`, `Status+DueAt`).

## Known limitations (read before deploying)

- **Registration has no email verification and no password reset.** Anyone can register with any address, and a forgotten password can't be recovered (an admin would have to change it in the `Users` table by hand). Add both before opening sign-up to the public. Wrong-password lockout (5 tries, 15 minutes) and the limit of 5 sign-ups per hour per address are kept in memory and reset when the API restarts; behind a reverse proxy every visitor shares the proxy's address, so configure forwarded headers first. Always serve the site over HTTPS, or passwords travel in clear text.
- **Follow-ups are emailed automatically without review.** The AI drafts them and n8n sends them when due. You can cancel any pending follow-up in the Leads screen, but there is no approval step yet. If that's not acceptable for your use, add an "approved" flag and filter on it in `GET /followups/due`.
- **The website-form webhook is public.** Anyone who finds the URL can create inquiries (and trigger LLM calls). Use a hard-to-guess path, a shared secret header, or rate limiting before going live.
- **Partial-batch resend.** If the email node fails partway through a batch of due follow-ups, the ones already emailed in that run aren't marked sent and will be re-sent on the next run. Wrapping the send in a *Loop Over Items* node (batch size 1) removes this.
- **Every business uses your AI key and your n8n.** All registered businesses make LLM calls with the one `Llm__ApiKey` you configure, so open sign-up means anyone can spend your credits. Keep registration off (`Auth__AllowRegistration=false`) unless you want that, and set a spending limit with your AI provider. Emails also all go out through the one SMTP account in n8n.
- **One person per business.** A business has exactly one login (no teams or roles), and there is no admin screen to list, suspend or delete businesses.
- **Inquiry text is stored as plain text** and contains personal data. Plan retention and deletion before using real customer data.

## Project structure

```
backend/BizAssistant.Api/
  Controllers/   Inquiries, Replies, Leads, Automation (n8n), Stats, Settings
  Services/      LlmService, DemoLlm, N8nNotifier, InquiryService, Support (options, API-key filter)
  Models/        Entities.cs, Dtos.cs
  Data/          AppDbContext.cs
database/schema.sql
frontend/src/    App.jsx, api.js, ui.jsx, components/{Dashboard,Inbox,Leads,Settings,NewInquiry}.jsx
n8n/             three importable workflows
docker-compose.yml
```

## Ideas for next steps

Authentication and multiple users · email *receiving* (IMAP/Gmail trigger in n8n → intake) · approval step for follow-ups · retrieval over a larger knowledge base instead of one FAQ field · per-category reply templates · unit tests for `InquiryService` with a fake `ILlmService` · CI that builds the API and dashboard.
