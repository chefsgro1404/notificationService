import type { Metadata } from "next";
import AudioCategories from "@/components/AudioCategories";

export const metadata: Metadata = {
  title: "Categories",
  description: "Audio categories used by voice notifications.",
};

export default function AudioCategoriesPage() {
  return (
    <main className="container wide">
      <header className="header">
        <h1>Categories</h1>
        <p>
          Each category plays one audio file on voice calls. Add a category here, then use <strong>Link</strong> on an
          active audio in the Audio Log to choose its audio. Voice notifications send the category&rsquo;s id as{" "}
          <code>categoryId</code>.
        </p>
      </header>

      <AudioCategories />
    </main>
  );
}
