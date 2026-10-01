"use client";

import Image from "next/image";
import Link from "next/link";
import { usePathname } from "next/navigation";
import SignOutButton from "./SignOutButton";
import styles from "./SiteNav.module.css";

const LINKS = [
  { href: "/", label: "Text to WAV" },
  { href: "/audio-logs", label: "Audio Log" },
  { href: "/audio-categories", label: "Categories" },
  { href: "/notification-logs", label: "Notification Log" },
] as const;

export interface NavUser {
  name: string;
  username: string;
  roles: string[];
}

function initials(name: string, username: string): string {
  const source = name.trim() || username.trim();
  const parts = source.split(/[\s@.]+/).filter(Boolean);
  return ((parts[0]?.[0] ?? "?") + (parts[1]?.[0] ?? "")).toUpperCase();
}

/** Top navigation: logo, page links, the signed-in user and Sign out. */
export default function SiteNav({ user }: { user: NavUser }) {
  const pathname = usePathname();

  return (
    <header className={styles.bar}>
      <nav className={styles.nav} aria-label="Main">
        <Link href="/" className={styles.brand}>
          <Image src="/chefsrhere-logo.png" alt="Chefs R Here" width={36} height={36} priority />
          <span className={styles.wordmark}>
            <span className={styles.wordmarkName}>
              chefs<span className={styles.wordmarkAccent}>r</span>here
            </span>
            <span className={styles.wordmarkTag}>Notify Studio</span>
          </span>
        </Link>

        <ul className={styles.links}>
          {LINKS.map((link) => {
            const current = link.href === "/" ? pathname === "/" : pathname.startsWith(link.href);
            return (
              <li key={link.href}>
                <Link
                  href={link.href}
                  className={current ? `${styles.link} ${styles.current}` : styles.link}
                  aria-current={current ? "page" : undefined}
                >
                  {link.label}
                </Link>
              </li>
            );
          })}
        </ul>

        <div className={styles.user}>
          <span className={styles.avatar} aria-hidden="true">
            {initials(user.name, user.username)}
          </span>
          <span className={styles.who}>
            <span className={styles.name}>{user.name || user.username}</span>
            <span className={styles.role}>{user.roles.join(", ") || "No role"}</span>
          </span>
          <SignOutButton className={styles.signOut} />
        </div>
      </nav>
    </header>
  );
}
