using System.Text.RegularExpressions;

namespace NotificationService.Application.Validation;

/// <summary>
/// Parses voice recipients: each value may hold several comma-separated E.164 phone numbers.
/// </summary>
public static class VoiceRecipients
{
    private static readonly Regex E164 =
        new(@"^\+[1-9]\d{7,14}$", RegexOptions.Compiled);

    /// <summary>
    /// Splits recipient values on commas, trims them, and removes empty entries and duplicates.
    /// </summary>
    /// <param name="values">Recipient values; each may contain several comma-separated phone numbers.</param>
    /// <returns>The distinct phone numbers, in their original order.</returns>
    public static List<string> Split(
        IEnumerable<string?> values) =>
        values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .SelectMany(x => x!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Determines whether a phone number is in E.164 format (for example +18005551234).
    /// </summary>
    /// <param name="phoneNumber">The phone number to check.</param>
    /// <returns><see langword="true"/> if the number is valid E.164; otherwise <see langword="false"/>.</returns>
    public static bool IsValid(string phoneNumber) =>
        E164.IsMatch(phoneNumber);
}
