import "server-only";
import { ConfidentialClientApplication, LogLevel } from "@azure/msal-node";
import type { AuthConfig } from "./config";

/**
 * MSAL Node confidential client for the app registration. Sign-in uses the authorization code flow
 * with PKCE; the client secret never leaves the server.
 */

let cached: { key: string; client: ConfidentialClientApplication } | null = null;

export function getMsalClient(config: AuthConfig): ConfidentialClientApplication {
  const key = `${config.authority}|${config.clientId}`;

  if (!cached || cached.key !== key) {
    cached = {
      key,
      client: new ConfidentialClientApplication({
        auth: {
          clientId: config.clientId,
          authority: config.authority,
          clientSecret: config.clientSecret,
        },
        system: {
          loggerOptions: {
            logLevel: LogLevel.Warning,
            piiLoggingEnabled: false,
            loggerCallback: (level, message) => {
              if (level <= LogLevel.Warning) {
                console.warn(`[msal] ${message}`);
              }
            },
          },
        },
      }),
    };
  }

  return cached.client;
}
