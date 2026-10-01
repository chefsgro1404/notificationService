# NotificationService

Azure Functions (.NET 8, isolated worker) for email, SMS, voice and **Text to WAV** notifications,
plus a Next.js frontend in [`client/`](client/).

## Text to WAV

Type up to 150 characters, click **Generate Audio**, and play back a WAV file produced by
Azure AI Speech.

```
Browser ──► Next.js  POST /api/tts  (client/src/app/api/tts/route.ts)
                │   adds x-functions-key on the server; the key never reaches the browser
                ▼
Azure Function  POST /api/tts  (TextToSpeechFunction)
                │  1. validate: 1–150 characters (Unicode code points), control chars stripped
                │  2. add a TextToSpeechLog row: RowKey = new notification id, Id = next int,
                │     Status = AudioNotGenerated
                │  3. Azure AI Speech REST API → riff-24khz-16bit-mono-pcm (WAV)
                │  4. upload to blob  <container>/T2A/<notificationId>.wav  (audio/wav)
                │  5. update the same row: Status = Active, BlobName, SizeBytes
                │     (if 3 or 4 fails, the row stays AudioNotGenerated with ErrorMessage)
                │  6. create a read-only SAS URL valid for 60 minutes
                ▼
Response  { id, notificationId, blobName, audioUrl, expiresAtUtc, contentType, sizeBytes, characterCount }
```

The web app has three pages:

| Page | Purpose |
|---|---|
| `/` Text to WAV | Enter text and generate audio |
| `/audio-logs` Audio Log | Every text-to-audio request by id and notification id, with filters, pagination, playback, the failure reason, and Activate / Deactivate / Delete / Restore |
| `/notification-logs` Notification Log | The `NotificationAudit` table with filters and pagination; rows are colored by status |

| Environment | Blob Storage | Speech |
|---|---|---|
| Local | Azurite (`UseDevelopmentStorage=true`) | **Real** Azure AI Speech |
| Azure | Storage account (`BlobStorage:ConnectionString`) | **Real** Azure AI Speech |

Speech is never emulated. Only Blob Storage switches between Azurite and Azure.

### API

`POST /api/tts` (function key required in Azure)

```http
Content-Type: application/json

{ "text": "Hello from Text to WAV" }
```

| Status | Meaning |
|---|---|
| 200 | Audio generated; body contains `audioUrl` (read-only SAS, 60 min) |
| 400 | Not JSON, text missing/blank, or longer than 150 characters |
| 502 | Azure AI Speech rejected the request, was unreachable, or timed out |
| 500 | Any other failure (for example, storage) |

### Audio log (`TextToSpeechLog` table)

Every request adds a row to the `TextToSpeechLog` table (created if it does not exist) in the same storage
account as `NotificationAudit` (`AuditStorage:ConnectionString`). The row is written *before* the audio is
generated and updated afterwards.

- **RowKey**: the notification id (a GUID). The audio file is stored under that key:
  `<container>/T2A/<notificationId>.wav`.
- **Id**: a sequential `int` (1, 2, 3, …) from a counter row (`PartitionKey=counter`, `RowKey=audio-id`)
  updated with ETag optimistic concurrency, so concurrent requests never get the same id. The API and pages
  address records by this id (`Id eq n` filter).
- **Status**:
  - `AudioNotGenerated`: set when the request is logged. It stays this way if synthesis or upload fails,
    and `ErrorMessage` says why (for example `Azure AI Speech returned 401 (Unauthorized).`).
  - `Active`: the WAV file is stored and playable.
  - `Inactive`: kept but not playable.
  - `Deleted`: soft delete. The record and file are kept, hidden from the default list, and can be restored.
- **Allowed changes**: generated audio can move freely between Active, Inactive and Deleted. Audio that was
  never generated can only be deleted (activating it returns 409). `AudioNotGenerated` cannot be set by hand.

| Endpoint | Description |
|---|---|
| `GET /api/tts/logs?status=&search=&page=&pageSize=` | `status` = `AudioNotGenerated`, `Active`, `Inactive`, `Deleted` or `All` (default: everything except Deleted); `search` = text contains |
| `GET /api/tts/logs/{id}` | One audio (404 if unknown); active audio includes a fresh 60-minute `audioUrl` |
| `PATCH /api/tts/logs/{id}/status` | Body `{ "status": "Active" \| "Inactive" \| "Deleted" }`; 409 if the change is not allowed |

### Audio categories (`AudioCategory` table) and voice notifications

A category is a named slot that points at one text-to-speech audio. Voice notifications pick their audio
by category instead of a fixed file in appsettings.

1. **Add a category** on the **Categories** page (`/audio-categories`). It gets the next integer id.
2. **Link audio** from the **Audio Log**: click **Link** on an *Active* audio row, choose a category in the
   pop-up, then **Save**. The category row stores `CategoryId`, `Name` and `AudioId`. Linking again
   replaces the category's audio. Only active, generated audio can be linked (409 otherwise).
3. **Send a voice notification** with the category:

   ```bash
   curl -X POST https://<function-app>/api/voice-notifications?code=<key> \
     -F recipient=+18005551234 -F categoryId=1 -F text="optional description"
   ```

   The ingress returns 400 if `categoryId` is missing, the category doesn't exist, or it has no active
   audio. The worker (`SendPrerecordedVoiceNotification`) resolves category → `AudioId` → audio log →
   `T2A/<notificationId>.wav` when it sends. If the audio was deactivated in the meantime, the
   notification is marked **Failed** with `AudioUnavailable` (not retried).

| Endpoint | Description |
|---|---|
| `GET /api/audio-categories` | All categories (sorted by name) |
| `POST /api/audio-categories` | Body `{ "name": "Support" }`; 1–50 characters, unique; 201, 400 or 409 |
| `PUT /api/audio-categories/{id}/audio` | Body `{ "audioId": 12 }`; 200, 400, 404 (category) or 409 (audio not active) |

The ACS call sender passes the audio blob in the call's callback URL (`&audio=T2A/...wav`), so playback and
unanswered-call retries use the same file. Only `T2A/<guid>.wav` names are accepted there. The
`Voice:AudioFolder` and `Voice:AudioFileName` settings were removed.

### Notification log (`NotificationAudit` table)

`GET /api/notification-logs?channel=&status=&recipient=&from=&to=&page=&pageSize=`

| Parameter | Values |
|---|---|
| `channel` | `Email`, `Sms`, `Telegram` (filtered in Table Storage, by partition) |
| `status` | `Accepted`, `Queued`, `Processing`, `Sent`, `Retrying`, `Failed` (filtered in Table Storage) |
| `recipient` | Case-insensitive "contains" |
| `from` / `to` | ISO 8601; `from` is inclusive, `to` is exclusive. The web page sends whole local days. |
| `page` / `pageSize` | Default 1 / 20; `pageSize` can be at most 100 |

Row colors: **Sent** green, **Failed** red, **Processing** amber, **Retrying** orange, **Accepted/Queued** blue.

**Paging model.** Table Storage cannot sort or count. Both logs read the rows that match the server-side
filters (at most 5,000 per request), then sort newest first and page in memory. That gives exact page
numbers and totals. If more than 5,000 rows match, the response has `truncated: true` and the page asks
you to narrow the filters, for example with a date range. For much larger audit tables, move to keys that
sort by time or add an index table.

All log responses use the same paging shape:
`{ items, page, pageSize, totalCount, totalPages, truncated }`.

### Code map

| Layer | File |
|---|---|
| Validation rules | `src/NotificationService.Application/Validation/SpeechText.cs` |
| Orchestration | `src/NotificationService.Application/Services/TextToSpeechService.cs` |
| Azure AI Speech client | `src/NotificationService.Infrastructure/Speech/AzureSpeechSynthesizer.cs` |
| Speech settings | `src/NotificationService.Infrastructure/Configuration/SpeechOptions.cs` |
| HTTP endpoint | `src/NotificationService.Functions/Functions/TextToSpeechFunction.cs` |
| Frontend UI | `client/src/components/TextToWav.tsx` |
| Frontend → backend proxy | `client/src/app/api/tts/route.ts`, `client/src/lib/backend.ts` |
| Audio log store (int ids, status) | `src/NotificationService.Infrastructure/Storage/AzureTableTextToSpeechLogStore.cs` |
| Notification log query | `src/NotificationService.Infrastructure/Storage/AzureTableAuditStore.cs` (`QueryAsync`) |
| Log endpoints | `src/NotificationService.Functions/Functions/TextToSpeechLogFunction.cs`, `NotificationLogFunction.cs` |
| Log pages | `client/src/components/AudioLog.tsx`, `NotificationLog.tsx`, `Pagination.tsx` |

## Sign-in (Microsoft Entra ID, MSAL)

Every page and API route requires sign-in. Only users with the **Admin** app role can use the app; users
with **Guest** (or no role) see a "You don't have access yet" page.

| Piece | File |
|---|---|
| Login page (logo, "Sign in with Microsoft") | `client/src/app/login/page.tsx` |
| Start sign-in: auth code + PKCE, state, nonce | `client/src/app/api/auth/login/route.ts` |
| Callback: redeem code (MSAL Node + client secret), validate nonce/aud/tid, create session | `client/src/app/api/auth/callback/route.ts` |
| Logout: clear session and sign out of Microsoft | `client/src/app/api/auth/logout/route.ts` (POST) |
| Redirect signed-out users and non-admins (optimistic) | `client/src/proxy.ts` |
| Server-side checks in every protected page and API route | `client/src/lib/auth/guard.ts` (`requireAdminPage`, `withAdmin`) |
| No-access page for Guest / no role | `client/src/app/unauthorized/page.tsx` |

- The session is an encrypted, HTTP-only cookie (`t2w_session`, JWE A256GCM, 8 hours). It holds the
  user's object id, name, username and app roles; Entra tokens are not kept.
- Roles come from the ID token's `roles` claim (the app registration's **Admin** and **Guest** app roles).
- The client secret stays on the server (`AZURE_AD_CLIENT_SECRET`); the browser only ever sees the
  session cookie.

**App registration setup (Authentication blade, platform "Web"):**

| Setting | Local | Azure |
|---|---|---|
| Redirect URI | `https://localhost:3000/api/auth/callback` | `https://<web-app>/api/auth/callback` |
| Front-channel logout / post-logout redirect | `https://localhost:3000/login` | `https://<web-app>/login` |

Assign users to the **Admin** role under **Enterprise applications → (this app) → Users and groups**.

**Settings (`client/.env.local` locally, App Service application settings in Azure):**

| Variable | Value |
|---|---|
| `AZURE_AD_TENANT_ID` | Directory (tenant) ID |
| `AZURE_AD_CLIENT_ID` | Application (client) ID |
| `AZURE_AD_CLIENT_SECRET` | Client secret (**secret**; Key Vault reference in Azure) |
| `APP_BASE_URL` | Public URL of the web app, e.g. `https://localhost:3000` |
| `AUTH_SESSION_SECRET` | 32+ random characters (**secret**) |

These are Next.js settings, so they live in `.env` files and app settings rather than the Functions
`appsettings.json`. The Functions app is still reached only through the signed-in Next.js server,
using the function key.

## Configuration

### Backend (`Speech` section)

| Setting | Env var / app setting | Default | Notes |
|---|---|---|---|
| `Speech:Key` | `Speech__Key` | — | **Secret.** Never put it in `appsettings*.json`. |
| `Speech:Region` | `Speech__Region` | `southeastasia` (appsettings.json) | Region of the Speech resource |
| `Speech:VoiceName` | `Speech__VoiceName` | `en-US-JennyNeural` | Any [neural voice](https://learn.microsoft.com/azure/ai-services/speech-service/language-support?tabs=tts); its locale prefix becomes the SSML language |
| `Speech:TimeoutSeconds` | `Speech__TimeoutSeconds` | `30` | HTTP timeout for the Speech call |

The endpoint is derived from the region:
`https://<region>.tts.speech.microsoft.com/cognitiveservices/v1`. The resource endpoint
`https://southeastasia.api.cognitive.microsoft.com/` is not used for synthesis.

The audio is stored in the existing `BlobStorage:ContainerName` container (default `notifications`),
which stays private. The connection string must include an account key, because the SAS URL is signed
with it. `UseDevelopmentStorage=true` works for Azurite.

### Frontend (`client/.env.local`, server-only)

| Variable | Local | Azure |
|---|---|---|
| `FUNCTIONS_BASE_URL` | `http://localhost:7071` | `https://<function-app>.azurewebsites.net` |
| `FUNCTIONS_KEY` | empty (not enforced locally) | Function key of `TextToSpeech` |

## Run locally

Prerequisites: .NET 8 SDK, Azure Functions Core Tools v4, Node.js 20.9+ (24 recommended), and Azurite
(`npm i -g azurite`). The Docker-based Service Bus emulator is only needed for the queue-triggered
functions, not for Text to WAV.

1. **Start Azurite from the repository root** (Blob `127.0.0.1:10000`, Queue `10001`, Table `10002`).
   Your local data (the `NotificationAudit` history, blobs) is in the `__azurite_db_*.json` files and
   `__blobstorage__` folder at the repo root, so start Azurite from there:

   ```bash
   azurite --silent --location .
   ```

2. **Configure the Speech key.** `src/NotificationService.Functions/local.settings.json` is gitignored.
   Add the key and region under `Values`:

   ```json
   {
     "IsEncrypted": false,
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
       "Speech__Key": "<your-speech-key>",
       "Speech__Region": "southeastasia"
     }
   }
   ```

   `appsettings.Development.json` already points `BlobStorage` at Azurite.

3. **Start the Functions host:**

   ```bash
   cd src/NotificationService.Functions
   func start --port 7071
   ```

   You should see `TextToSpeech: [POST] http://localhost:7071/api/tts`. The Visual Studio profile
   uses port 7262; set `FUNCTIONS_BASE_URL` to match.

   Quick check:

   ```bash
   curl -X POST http://localhost:7071/api/tts -H "Content-Type: application/json" -d '{"text":"Hello"}'
   ```

4. **Start the frontend:**

   ```bash
   cd client
   cp .env.example .env.local   # FUNCTIONS_BASE_URL=http://localhost:7071
   npm install
   npm run dev
   ```

   Open https://localhost:3000. Locally the SAS URL points at `http://127.0.0.1:10000/devstoreaccount1/...`,
   which the browser plays directly.

## Tests

```bash
# Backend (331 tests; CI and CD require coverage above 90%)
dotnet test tests/NotificationService.Tests/NotificationService.Tests.csproj --collect:"XPlat Code Coverage" --results-directory ./coverage
python .github/scripts/check-coverage.py ./coverage 90

# Frontend
cd client && npm run lint && npm run typecheck && npm run test:coverage && npm run build
```

## Deploy to Azure

### Backend (Function App)

The `CD - Deploy Azure Function` workflow (`.github/workflows/cd.yml`, manual trigger) builds, tests
(coverage above 90%), and deploys to `func-notification-prod-001`. Add or check these **application
settings** on the Function App (`__` replaces `:` in setting names):

| Setting | Value | New? |
|---|---|---|
| `Speech__Key` | Azure AI Speech key (**secret**, Key Vault reference recommended) | New |
| `Speech__Region` | `southeastasia` (default in appsettings.json) | New, optional |
| `Speech__VoiceName` | `en-US-JennyNeural` (default) | New, optional |
| `Speech__TimeoutSeconds` | `30` (default) | New, optional |
| `Voice__MaxCallAttempts` | `0` = call each recipient once, no retry (default) | Changed |
| `Voice__AudioFolder`, `Voice__AudioFileName` | **Remove** if set: the audio comes from the category, always `T2A/{notificationId}.wav` | Removed |
| `BlobStorage__ConnectionString` | Storage account connection string with account key (signs SAS URLs) | Existing |
| `AuditStorage__ConnectionString` | Table Storage connection string; the `TextToSpeechLog` and `AudioCategory` tables (fixed names) are created on first use | Existing |
| `AzureCommunicationServices__ConnectionString` | ACS connection string (**secret**) | Existing |
| `AzureCommunicationServices__CallerPhoneNumber` | ACS number calls are made from (not a recipient) | Existing |
| `AzureCommunicationServices__CallbackBaseUrl` | `https://<function-app>.azurewebsites.net` | Existing |

Voice recipients always come from the `recipient` field of each `POST /api/voice-notifications` request;
no recipient is read from configuration.

### Frontend (Next.js on Azure App Service)

`.github/workflows/cd-client.yml` (**CD - Deploy Web App**, manual trigger) lints, type-checks, tests
with coverage above 90%, builds the standalone server, adds `public/` and `.next/static/`, and deploys
`client/.next/standalone` with `azure/webapps-deploy` (startup command `node server.js`). CI
(`.github/workflows/ci.yml`) runs the same checks and build on every push and PR to `main`.

**One-time setup**

1. Create an App Service: **Linux**, runtime **Node 24 LTS**. Put its name in `AZURE_WEBAPP_NAME` at the
   top of `cd-client.yml` (placeholder: `app-notification-web-prod-001`).
2. The workflow signs in with the same OIDC secrets as the Function App workflow (`AZURE_CLIENT_ID`,
   `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`). Give that identity the **Website Contributor** role on
   the new App Service.
3. Add the sign-in redirect URIs for the live URL to the app registration (see *Sign-in* above).
4. Set the application settings below, then run **Actions → CD - Deploy Web App → Run workflow**.

**App Service application settings**

| Setting | Value | Secret |
|---|---|---|
| `FUNCTIONS_BASE_URL` | `https://<function-app>.azurewebsites.net` (no `/api`) | |
| `FUNCTIONS_KEY` | Function App **default host key** | Yes |
| `AZURE_AD_TENANT_ID` | `fa9a6c58-514f-4252-b674-5b4c7fb025a2` | |
| `AZURE_AD_CLIENT_ID` | `5626cd9b-4bce-485e-97ff-80e4fff07ec0` | |
| `AZURE_AD_CLIENT_SECRET` | App registration client secret (Key Vault reference recommended) | Yes |
| `APP_BASE_URL` | `https://<web-app>.azurewebsites.net` (or the custom domain) | |
| `AUTH_SESSION_SECRET` | 32+ random characters, different from local | Yes |
| `SCM_DO_BUILD_DURING_DEPLOYMENT` | `false` (the package is already built) | |

All of these are read at runtime on the server; none are needed at build time and none reach the browser.
Browsers call only the Next.js app, so the Function App does not need CORS.

## Security notes

- The Speech key lives only in `local.settings.json` (gitignored) locally, and in app settings or Key
  Vault in Azure. `appsettings.json` ships with `"Key": ""`.
- The blob container is private. Each audio file is reachable only through its read-only SAS, which
  expires after 60 minutes.
- Text is XML-escaped before it is placed in SSML, and control characters are replaced.
- The public `/api/tts` Next.js route can consume Speech quota. For a public deployment, put it behind
  authentication or rate limiting (for example, Azure Front Door WAF rate-limit rules).
- Generated blobs are not deleted automatically. Add a Storage **lifecycle management** rule for the
  prefix `notifications/tts/` (for example, delete after 1 day) to control storage costs.
