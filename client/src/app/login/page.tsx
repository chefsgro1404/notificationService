import type { Metadata } from "next";
import Image from "next/image";
import { redirect } from "next/navigation";
import { getSessionUser } from "@/lib/auth/guard";
import { isAdmin, safeReturnTo } from "@/lib/auth/session";
import styles from "./login.module.css";

export const metadata: Metadata = {
  title: "Sign in",
  description: "Sign in to chefsrhere Notify Studio with your Microsoft account.",
};

/** Messages for the ?error= codes set by the sign-in routes. */
const ERRORS: Record<string, string> = {
  cancelled: "Sign-in was cancelled. You can try again whenever you're ready.",
  expired: "Your sign-in took too long or was started in another tab. Please try again.",
  signin_failed: "We couldn't sign you in with Microsoft. Please try again.",
  config: "Sign-in isn't configured on this server yet. Contact your administrator.",
};

function MicrosoftLogo() {
  return (
    <svg width="20" height="20" viewBox="0 0 21 21" aria-hidden="true">
      <rect x="1" y="1" width="9" height="9" fill="#f25022" />
      <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
      <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
      <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
    </svg>
  );
}

async function currentUser() {
  try {
    return await getSessionUser();
  } catch {
    return null; // auth not configured: show the page with the config message on submit
  }
}

export default async function LoginPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string; returnTo?: string }>;
}) {
  const { error, returnTo } = await searchParams;
  const target = safeReturnTo(returnTo);
  const user = await currentUser();

  if (user) {
    redirect(isAdmin(user) ? target : "/unauthorized");
  }

  const message = error ? (ERRORS[error] ?? ERRORS.signin_failed) : null;
  const loginHref = `/api/auth/login${target === "/" ? "" : `?returnTo=${encodeURIComponent(target)}`}`;

  return (
    <main className={styles.page}>
      <section className={styles.brandPanel} aria-hidden="true">
        <div className={styles.brandInner}>
          <div className={styles.logoTile}>
            <Image src="/chefsrhere-logo.png" alt="" width={120} height={120} priority />
          </div>
          <h2 className={styles.tagline}>Every message, delivered.</h2>
          <p className={styles.lead}>
            Turn text into speech, track every generated audio file, and follow each notification from accepted to
            delivered.
          </p>
          <ul className={styles.features}>
            <li>
              <span className={styles.dot} /> Text to WAV with Azure AI Speech
            </li>
            <li>
              <span className={styles.dot} /> Audio log with active, inactive and deleted states
            </li>
            <li>
              <span className={styles.dot} /> Color-coded notification delivery history
            </li>
          </ul>
        </div>
      </section>

      <section className={styles.formPanel}>
        <div className={styles.card}>
          <Image
            className={styles.cardLogo}
            src="/chefsrhere-logo.png"
            alt="Chefs R Here — Cravings delivered"
            width={96}
            height={96}
            priority
          />

          <h1 className={styles.title}>Welcome back</h1>
          <p className={styles.subtitle}>Sign in with your organization&rsquo;s Microsoft account to continue.</p>

          {message && (
            <p role="alert" className={styles.error}>
              {message}
            </p>
          )}

          <a href={loginHref} className={styles.microsoftButton}>
            <MicrosoftLogo />
            <span>Sign in with Microsoft</span>
          </a>

          <p className={styles.note}>
            Access is limited to users with the <strong>Admin</strong> role in this application.
          </p>

          <footer className={styles.footer}>
            <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true">
              <path
                fill="currentColor"
                d="M12 1 3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4Zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8Z"
              />
            </svg>
            Secured by Microsoft Entra ID
          </footer>
        </div>
      </section>
    </main>
  );
}
