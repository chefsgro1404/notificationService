import type { Metadata } from "next";
import Image from "next/image";
import { redirect } from "next/navigation";
import SignOutButton from "@/components/SignOutButton";
import { getSessionUser } from "@/lib/auth/guard";
import { isAdmin } from "@/lib/auth/session";
import styles from "../login/login.module.css";

export const metadata: Metadata = {
  title: "No access",
};

/** Shown to signed-in users without the Admin app role (for example Guest). */
export default async function UnauthorizedPage() {
  const user = await getSessionUser();

  if (!user) {
    redirect("/login");
  }

  if (isAdmin(user)) {
    redirect("/");
  }

  return (
    <main className={styles.formPanel} style={{ minHeight: "100vh" }}>
      <div className={styles.card}>
        <Image className={styles.cardLogo} src="/chefsrhere-logo.png" alt="Chefs R Here" width={80} height={80} />

        <h1 className={styles.title}>You don&rsquo;t have access yet</h1>
        <p className={styles.subtitle}>
          You&rsquo;re signed in as <strong>{user.name || user.username}</strong>
          {user.username && user.name ? ` (${user.username})` : ""}, but this application currently requires the{" "}
          <strong>Admin</strong> role.
        </p>

        <p role="status" className={styles.error}>
          Your role: {user.roles.length > 0 ? user.roles.join(", ") : "none assigned"}. Ask an administrator to assign
          you the Admin role for this app in Microsoft Entra ID, then sign in again.
        </p>

        <SignOutButton className={styles.microsoftButton} />
      </div>
    </main>
  );
}
