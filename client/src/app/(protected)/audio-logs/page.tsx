import type { Metadata } from "next";
import AudioLog from "@/components/AudioLog";

export const metadata: Metadata = {
  title: "Audio Log",
  description: "Text-to-audio requests with audio-not-generated, active, inactive and deleted status.",
};

export default function AudioLogPage() {
  return (
    <main className="container wide">
      <header className="header">
        <h1>Audio Log</h1>
        <p>
          Every text-to-audio request with its id and notification id. A request starts as &ldquo;Audio not
          generated&rdquo; and becomes Active once its WAV file is saved to the T2A folder. Only active audio can be
          played; deleted audio is hidden by default and can be restored.
        </p>
      </header>

      <AudioLog />
    </main>
  );
}
