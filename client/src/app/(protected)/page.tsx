import TextToWav from "@/components/TextToWav";

export default function Home() {
  return (
    <main className="container">
      <header className="header">
        <h1>Text to WAV</h1>
        <p>Type up to 150 characters and turn them into a WAV file with Azure AI Speech.</p>
      </header>

      <TextToWav />
    </main>
  );
}
