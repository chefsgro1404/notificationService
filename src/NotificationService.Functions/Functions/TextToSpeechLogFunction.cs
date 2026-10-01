using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Enums;
using NotificationService.Functions.Http;

namespace NotificationService.Functions.Functions;

/// <summary>
/// Text-to-speech (T2A) audio log endpoints (TextToSpeechLog table):
/// list with filters and pagination, read one record by its integer id, and change its status
/// (Active / Inactive / Deleted; AudioNotGenerated is set automatically). Active audio includes a fresh
/// 60-minute playback URL.
/// </summary>
public sealed class TextToSpeechLogFunction
{
    /// <summary>Query value that includes every status, deleted audio included.</summary>
    public const string AllStatuses = "All";

    private readonly ITextToSpeechService _textToSpeech;
    private readonly ILogger<TextToSpeechLogFunction> _logger;

    /// <summary>
    /// Creates the functions.
    /// </summary>
    /// <param name="textToSpeech">Reads and updates the audio log.</param>
    /// <param name="logger">Logger for failures.</param>
    public TextToSpeechLogFunction(
        ITextToSpeechService textToSpeech,
        ILogger<TextToSpeechLogFunction> logger)
    {
        _textToSpeech = textToSpeech;
        _logger = logger;
    }

    /// <summary>
    /// Lists text-to-speech records, newest first.
    /// Query: status = AudioNotGenerated | Active | Inactive | Deleted | All (default: everything except Deleted),
    /// search, page, pageSize.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>200 OK with a page of audio, 400 for invalid filters, or 500 on failure.</returns>
    [Function("TextToSpeechLogs")]
    public async Task<IActionResult> List(
            [HttpTrigger(
                    AuthorizationLevel.Function,
                    "get",
                    Route = "tts/logs")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var query = request.Query;

        var error = QueryParameters.TryGetPaging(query, out var page, out var pageSize);

        var statuses = TextToSpeechLogQuery.DefaultStatuses;

        var rawStatus = query["status"].ToString();

        if (error is null && !string.IsNullOrWhiteSpace(rawStatus))
        {
            if (string.Equals(rawStatus.Trim(), AllStatuses, StringComparison.OrdinalIgnoreCase))
            {
                statuses = Enum.GetValues<AudioStatus>();
            }
            else if (QueryParameters.TryParseName<AudioStatus>(rawStatus, out var single))
            {
                statuses = [single];
            }
            else
            {
                error = $"status must be one of: {string.Join(", ", Enum.GetNames<AudioStatus>())}, {AllStatuses}.";
            }
        }

        if (error is not null)
        {
            return QueryParameters.Error(StatusCodes.Status400BadRequest, error);
        }

        return await Execute(
            "list audio",
            async () => new OkObjectResult(
                await _textToSpeech.GetLogsAsync(
                    new TextToSpeechLogQuery
                    {
                        Statuses = statuses,
                        Search = query["search"].ToString(),
                        Page = page,
                        PageSize = pageSize
                    },
                    cancellationToken)));
    }

    /// <summary>
    /// Reads one generated audio by its integer id.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="id">The audio id.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>200 OK with the audio, 404 when it does not exist, or 500 on failure.</returns>
    [Function("TextToSpeechLogById")]
    public Task<IActionResult> Get(
            [HttpTrigger(
                    AuthorizationLevel.Function,
                    "get",
                    Route = "tts/logs/{id:int}")]
            HttpRequest request,
        int id,
        CancellationToken cancellationToken) =>
        Execute(
            "read audio",
            async () => Found(id, await _textToSpeech.GetLogAsync(id, cancellationToken)));

    /// <summary>
    /// Changes an audio's status. Body: <c>{ "status": "Active" | "Inactive" | "Deleted" }</c>.
    /// Deleted is a soft delete: the record and file are kept and can be restored by setting Active.
    /// Audio that was never generated can only be deleted.
    /// </summary>
    /// <param name="request">The HTTP request with a JSON body.</param>
    /// <param name="id">The audio id.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// 200 OK with the updated audio, 400 for an invalid body, 404 when it does not exist,
    /// 409 when the change is not allowed, or 500 on failure.
    /// </returns>
    [Function("TextToSpeechLogStatus")]
    public async Task<IActionResult> UpdateStatus(
            [HttpTrigger(
                    AuthorizationLevel.Function,
                    "patch",
                    Route = "tts/logs/{id:int}/status")]
            HttpRequest request,
        int id,
        CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            return QueryParameters.Error(
                StatusCodes.Status400BadRequest,
                "Content-Type must be application/json.");
        }

        AudioStatusUpdateRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<AudioStatusUpdateRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (!QueryParameters.TryParseName<AudioStatus>(body?.Status, out var status))
        {
            return QueryParameters.Error(
                StatusCodes.Status400BadRequest,
                $"Body must be JSON like {{ \"status\": \"Inactive\" }} with status one of: {string.Join(", ", Enum.GetNames<AudioStatus>())}.");
        }

        return await Execute(
            "update audio status",
            async () =>
            {
                TextToSpeechLogItem? item;

                try
                {
                    item = await _textToSpeech.UpdateStatusAsync(id, status, cancellationToken);
                }
                catch (AudioStatusChangeException ex)
                {
                    return QueryParameters.Error(StatusCodes.Status409Conflict, ex.Message);
                }

                if (item is not null)
                {
                    _logger.LogInformation("Audio {AudioId} status set to {Status}", id, status);
                }

                return Found(id, item);
            });
    }

    private static IActionResult Found(
        int id,
        TextToSpeechLogItem? item) =>
        item is null
            ? QueryParameters.Error(StatusCodes.Status404NotFound, $"Audio {id} was not found.")
            : new OkObjectResult(item);

    private async Task<IActionResult> Execute(
        string operation,
        Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to {Operation}", operation);

            return QueryParameters.Error(
                StatusCodes.Status500InternalServerError,
                $"Unable to {operation}.");
        }
    }
}
