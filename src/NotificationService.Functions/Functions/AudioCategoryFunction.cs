using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Functions.Http;

namespace NotificationService.Functions.Functions;

/// <summary>
/// Audio category endpoints (AudioCategory table): list categories, create a category, and link active
/// text-to-speech audio to a category. Voice notifications pass a categoryId and play its linked audio.
/// </summary>
public sealed class AudioCategoryFunction
{
    private readonly IAudioCategoryService _categories;
    private readonly ILogger<AudioCategoryFunction> _logger;

    /// <summary>
    /// Creates the functions.
    /// </summary>
    /// <param name="categories">Manages categories.</param>
    /// <param name="logger">Logger for failures.</param>
    public AudioCategoryFunction(
        IAudioCategoryService categories,
        ILogger<AudioCategoryFunction> logger)
    {
        _categories = categories;
        _logger = logger;
    }

    /// <summary>
    /// Lists every category, ordered by name.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>200 OK with the categories, or 500 on failure.</returns>
    [Function("AudioCategories")]
    public Task<IActionResult> List(
            [HttpTrigger(
                    AuthorizationLevel.Function,
                    "get",
                    Route = "audio-categories")]
            HttpRequest request,
        CancellationToken cancellationToken) =>
        Execute(
            "list audio categories",
            async () => new OkObjectResult(await _categories.ListAsync(cancellationToken)));

    /// <summary>
    /// Creates a category. Body: <c>{ "name": "Support" }</c>.
    /// </summary>
    /// <param name="request">The HTTP request with a JSON body.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>201 Created with the category, 400 for an invalid name, 409 for a duplicate, or 500 on failure.</returns>
    [Function("CreateAudioCategory")]
    public async Task<IActionResult> Create(
            [HttpTrigger(
                    AuthorizationLevel.Function,
                    "post",
                    Route = "audio-categories")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var body = await ReadJsonAsync<CreateAudioCategoryRequest>(request, cancellationToken);

        if (body is null)
        {
            return QueryParameters.Error(
                StatusCodes.Status400BadRequest,
                "Body must be JSON like { \"name\": \"Support\" }.");
        }

        return await Execute(
            "create audio category",
            async () =>
            {
                try
                {
                    var category = await _categories.CreateAsync(body.Name, cancellationToken);

                    return new ObjectResult(category) { StatusCode = StatusCodes.Status201Created };
                }
                catch (ArgumentException ex)
                {
                    return QueryParameters.Error(StatusCodes.Status400BadRequest, FirstSentence(ex));
                }
                catch (AudioCategoryException ex)
                {
                    return QueryParameters.Error(StatusCodes.Status409Conflict, ex.Message);
                }
            });
    }

    /// <summary>
    /// Links active audio to a category, replacing any previous link. Body: <c>{ "audioId": 12 }</c>.
    /// </summary>
    /// <param name="request">The HTTP request with a JSON body.</param>
    /// <param name="id">The category id.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// 200 OK with the category, 400 for an invalid body, 404 when the category does not exist,
    /// 409 when the audio is missing or not active, or 500 on failure.
    /// </returns>
    [Function("LinkAudioCategory")]
    public async Task<IActionResult> LinkAudio(
            [HttpTrigger(
                    AuthorizationLevel.Function,
                    "put",
                    Route = "audio-categories/{id:int}/audio")]
            HttpRequest request,
        int id,
        CancellationToken cancellationToken)
    {
        var body = await ReadJsonAsync<LinkAudioRequest>(request, cancellationToken);

        if (body?.AudioId is not { } audioId || audioId < 1)
        {
            return QueryParameters.Error(
                StatusCodes.Status400BadRequest,
                "Body must be JSON like { \"audioId\": 12 }.");
        }

        return await Execute(
            "link audio to category",
            async () =>
            {
                try
                {
                    var category = await _categories.LinkAudioAsync(id, audioId, cancellationToken);

                    if (category is null)
                    {
                        return QueryParameters.Error(StatusCodes.Status404NotFound, $"Category {id} was not found.");
                    }

                    _logger.LogInformation("Linked audio {AudioId} to category {CategoryId}", audioId, id);

                    return new OkObjectResult(category);
                }
                catch (AudioCategoryException ex)
                {
                    return QueryParameters.Error(StatusCodes.Status409Conflict, ex.Message);
                }
            });
    }

    private static async Task<T?> ReadJsonAsync<T>(
        HttpRequest request,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!request.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await request.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>ArgumentException messages end with " (Parameter 'name')"; keep only the user-facing part.</summary>
    private static string FirstSentence(ArgumentException ex) =>
        ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", string.Empty);

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
