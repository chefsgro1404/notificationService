using System.Text;

namespace NotificationService.Application.Validation;

/// <summary>
/// Validation and normalization rules for text submitted to the Text to WAV feature.
/// Length is measured in Unicode code points, the same way the web client counts characters,
/// so an emoji counts as one character on both sides.
/// </summary>
public static class SpeechText
{
    /// <summary>The maximum number of characters that can be converted in one request.</summary>
    public const int MaxLength = 150;

    /// <summary>
    /// Trims the text and replaces control characters (line breaks, tabs, and characters
    /// that are not valid in XML) with spaces, so the text can be embedded safely in SSML.
    /// </summary>
    /// <param name="text">The raw text; may be <see langword="null"/>.</param>
    /// <returns>The normalized text, or an empty string when there is no text.</returns>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune))
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Counts the characters in the text as Unicode code points.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <returns>The number of code points.</returns>
    public static int CountCharacters(string text) =>
        text.EnumerateRunes().Count();

    /// <summary>
    /// Checks normalized text against the Text to WAV rules.
    /// </summary>
    /// <param name="normalizedText">Text returned by <see cref="Normalize"/>.</param>
    /// <returns>An error message describing the problem, or <see langword="null"/> when the text is valid.</returns>
    public static string? Validate(string normalizedText)
    {
        if (normalizedText.Length == 0)
        {
            return "Text is required.";
        }

        var length = CountCharacters(normalizedText);

        return length > MaxLength
            ? $"Text must be {MaxLength} characters or fewer (received {length})."
            : null;
    }
}
