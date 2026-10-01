using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Application.Validation;
using NotificationService.Functions.Http;

namespace NotificationService.Functions.Functions;

/// <summary>
/// Text to WAV endpoint: converts up to 150 characters of text to speech with Azure AI Speech,
/// stores the WAV file in blob storage, and returns a read-only SAS URL valid for 60 minutes.
/// Request: POST /api/tts with JSON <c>{ "text": "Hello" }</c>.
/// </summary>
public sealed class TextToSpeechFunction
{
    private readonly ITextToSpeechService _textToSpeech;
    private readonly ILogger<TextToSpeechFunction> _logger;

    /// <summary>
    /// Creates the function.
    /// </summary>
    /// <param name="textToSpeech">Generates and stores the audio.</param>
    /// <param name="logger">Logger for failures.</param>
    public TextToSpeechFunction(
        ITextToSpeechService textToSpeech,
        ILogger<TextToSpeechFunction> logger)
    {
        _textToSpeech = textToSpeech;
        _logger = logger;
    }

    /// <summary>
    /// Converts the posted text into a WAV file and returns its temporary URL.
    /// </summary>
    /// <param name="request">The HTTP request with a JSON body.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// 200 OK with the audio details, 400 for invalid input, 502 when Azure AI Speech fails,
    /// or 500 for any other failure.
    /// </returns>
    [Function("TextToSpeech")]
    public async Task<IActionResult> Run(
            [HttpTrigger(
                    AuthorizationLevel.Function,
                    "post",
                    Route = "tts")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "Content-Type must be application/json.");
        }

        TextToSpeechRequest? body;

        try
        {
            body = await request.ReadFromJsonAsync<TextToSpeechRequest>(
                cancellationToken);
        }
        catch (JsonException)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                "Request body must be valid JSON, for example { \"text\": \"Hello\" }.");
        }

        var validationError = SpeechText.Validate(
            SpeechText.Normalize(body?.Text));

        if (validationError is not null)
        {
            return Error(
                StatusCodes.Status400BadRequest,
                validationError);
        }

        try
        {
            var result = await _textToSpeech.GenerateAsync(
                body!.Text!,
                cancellationToken);

            _logger.LogInformation(
                "Generated speech {AudioId} ({CharacterCount} characters, {SizeBytes} bytes) at {BlobName}",
                result.Id,
                result.CharacterCount,
                result.SizeBytes,
                result.BlobName);

            return new OkObjectResult(result);
        }
        catch (SpeechSynthesisException ex)
        {
            _logger.LogError(
                ex,
                "Azure AI Speech failed to synthesize text");

            return Error(
                StatusCodes.Status502BadGateway,
                "The speech service could not generate audio. Please try again.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to generate text-to-speech audio");

            return Error(
                StatusCodes.Status500InternalServerError,
                "Unable to generate audio.");
        }
    }

    private static ObjectResult Error(
        int statusCode,
        string message) =>
        QueryParameters.Error(statusCode, message);
}
