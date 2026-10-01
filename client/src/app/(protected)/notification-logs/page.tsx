import type { Metadata } from "next";
import NotificationLog from "@/components/NotificationLog";

export const metadata: Metadata = {
  title: "Notification Log",
  description: "Notification delivery history from the NotificationAudit table.",
};

export default function NotificationLogPage() {
  return (
    <main className="container wide">
      <header className="header">
        <h1>Notification Log</h1>
        <p>Every email, SMS and voice notification, newest first. Rows are colored by delivery status.</p>
      </header>

      <NotificationLog />
    </main>
  );
}
