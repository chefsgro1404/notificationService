using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Application.Models;

namespace NotificationService.Functions.Http;

/// <summary>
/// Parses and validates query-string values shared by the log endpoints.
/// Each method returns <see langword="null"/> on success or an error message for a 400 response.
/// </summary>
internal static class QueryParameters
{
    /// <summary>
    /// Reads "page" (default 1) and "pageSize" (default 20, maximum 100).
    /// </summary>
    /// <param name="query">The request query string.</param>
    /// <param name="page">The parsed page number.</param>
    /// <param name="pageSize">The parsed page size.</param>
    /// <returns>An error message, or <see langword="null"/> when both values are valid.</returns>
    public static string? TryGetPaging(
        IQueryCollection query,
        out int page,
        out int pageSize)
    {
        page = 1;
        pageSize = PagedResult.DefaultPageSize;

        if (query.TryGetValue("page", out var pageValue) &&
            (!int.TryParse(pageValue, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
        {
            return "page must be a whole number of 1 or more.";
        }

        if (query.TryGetValue("pageSize", out var sizeValue) &&
            (!int.TryParse(sizeValue, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize) ||
             pageSize < 1 || pageSize > PagedResult.MaxPageSize))
        {
            return $"pageSize must be between 1 and {PagedResult.MaxPageSize}.";
        }

        return null;
    }

    /// <summary>
    /// Reads an optional enum value by name (case-insensitive). Numbers are rejected.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="query">The request query string.</param>
    /// <param name="name">The query-string key.</param>
    /// <param name="value">The parsed value, or <see langword="null"/> when the key is absent or empty.</param>
    /// <returns>An error message, or <see langword="null"/> when the value is absent or valid.</returns>
    public static string? TryGetEnum<TEnum>(
        IQueryCollection query,
        string name,
        out TEnum? value)
        where TEnum : struct, Enum
    {
        value = null;

        var raw = query[name].ToString();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (TryParseName<TEnum>(raw, out var parsed))
        {
            value = parsed;
            return null;
        }

        return $"{name} must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.";
    }

    /// <summary>
    /// Reads an optional ISO 8601 date/time; values without an offset are treated as UTC.
    /// </summary>
    /// <param name="query">The request query string.</param>
    /// <param name="name">The query-string key.</param>
    /// <param name="value">The parsed value, or <see langword="null"/> when the key is absent or empty.</param>
    /// <returns>An error message, or <see langword="null"/> when the value is absent or valid.</returns>
    public static string? TryGetDate(
        IQueryCollection query,
        string name,
        out DateTimeOffset? value)
    {
        value = null;

        var raw = query[name].ToString();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            value = parsed;
            return null;
        }

        return $"{name} must be an ISO 8601 date or date/time, for example 2026-09-30T00:00:00Z.";
    }

    /// <summary>
    /// Parses an enum member name (case-insensitive), rejecting numbers and combined values.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="raw">The text to parse.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true"/> when <paramref name="raw"/> is a member name.</returns>
    public static bool TryParseName<TEnum>(
        string? raw,
        out TEnum value)
        where TEnum : struct, Enum
    {
        var name = Enum.GetNames<TEnum>()
            .FirstOrDefault(x => string.Equals(x, raw?.Trim(), StringComparison.OrdinalIgnoreCase));

        value = name is null ? default : Enum.Parse<TEnum>(name);

        return name is not null;
    }

    /// <summary>
    /// Creates a JSON error response: <c>{ "error": "..." }</c>.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="message">The error message.</param>
    /// <returns>The error result.</returns>
    public static ObjectResult Error(
        int statusCode,
        string message) =>
        new(new { error = message })
        {
            StatusCode = statusCode
        };
}
