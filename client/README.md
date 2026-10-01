# Text to WAV — client

Next.js (App Router) frontend for the Text to WAV feature. See the [root README](../README.md)
for architecture, backend setup, and deployment.

```bash
cp .env.example .env.local   # FUNCTIONS_BASE_URL=http://localhost:7071
npm install
npm run dev                  # https://localhost:3000
```

| Script | Purpose |
|---|---|
| `npm run dev` | Development server |
| `npm run build` / `npm start` | Production build (standalone output) and server |
| `npm test` | Unit tests (Vitest + Testing Library) |
| `npm run lint` / `npm run typecheck` | ESLint / TypeScript |

- `src/components/TextToWav.tsx`: text box, live `n / 150` counter, Generate Audio button, success message, URL, and audio player.
- `src/app/api/tts/route.ts`: server-side proxy to the Azure Function. It adds `FUNCTIONS_KEY`, so the key never reaches the browser.
- `src/lib/text-to-wav.ts`: shared limits and validation. Characters are counted as Unicode code points, matching the backend.
