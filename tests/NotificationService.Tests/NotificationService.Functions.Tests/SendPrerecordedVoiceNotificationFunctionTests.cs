using Microsoft.Extensions.Logging;
using Moq;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Enums;
using NotificationService.Functions.Functions;
using System.Text.Json;
namespace NotificationService.Functions.Tests;
public class SendPrerecordedVoiceNotificationFunctionTests
{
    private readonly Mock<IVoiceCallSender> _voiceCallSender = new();
    private readonly Mock<IAuditStore> _auditStore = new();
    private readonly Mock<IAudioCategoryService> _categories = new();
    private readonly Mock<ILogger<SendPrerecordedVoiceNotificationFunction>> _logger = new();

    private const int CategoryId = 5;
    private const string AudioBlob = "T2A/6f1d3f4e-1c2b-4a5d-9e8f-0a1b2c3d4e5f.wav";

    public SendPrerecordedVoiceNotificationFunctionTests()
    {
        _categories
            .Setup(x => x.ResolveAudioBlobAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AudioBlob);
    }

    private SendPrerecordedVoiceNotificationFunction CreateFunction()
    {
        return new SendPrerecordedVoiceNotificationFunction(
            _voiceCallSender.Object,
            _auditStore.Object,
            _categories.Object,
            _logger.Object);
    }

    private static string CreateMessage(
        Guid notificationId,
        params string[] recipients) =>
        CreateMessage(notificationId, CategoryId, recipients);

    private static string CreateMessage(
        Guid notificationId,
        int? categoryId,
        params string[] recipients)
    {
        return JsonSerializer.Serialize(
            new NotificationMessage
            {
                NotificationId = notificationId,
                Channel = NotificationChannel.Telegram,
                Recipients = [.. recipients],
                CategoryId = categoryId,
                Text = "voice",
                CreatedAtUtc = DateTime.UtcNow
            });
    }

    [Fact]
    public async Task Run_CallsEachCommaSeparatedRecipient_AndMarksSent()
    {
        // Arrange
        var notificationId = Guid.NewGuid();

        _voiceCallSender
            .Setup(x => x.StartCallAsync(
                It.IsAny<string>(),
                notificationId,
                AudioBlob,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string recipient, Guid _, string _, CancellationToken _) => $"call-{recipient}");

        var function = CreateFunction();

        // Act
        await function.Run(
            CreateMessage(notificationId, "+18173235812, +16823005360", "+18173235812"),
            CancellationToken.None);

        // Assert
        _voiceCallSender.Verify(x => x.StartCallAsync(
                "+18173235812",
                notificationId,
                AudioBlob,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _voiceCallSender.Verify(x => x.StartCallAsync(
                "+16823005360",
                notificationId,
                AudioBlob,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _auditStore.Verify(x => x.UpdateStatusAsync(
                notificationId,
                "Telegram",
                "Sent",
                null,
                null,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_Throws_WhenMessageIsNull()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateFunction().Run("null", CancellationToken.None));
    }

    [Fact]
    public async Task Run_DoesNothing_WhenAlreadyProcessed()
    {
        // Arrange
        var notificationId = Guid.NewGuid();

        _auditStore
            .Setup(x => x.HasBeenProcessedAsync(
                notificationId,
                "Telegram",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var function = CreateFunction();

        // Act
        await function.Run(
            CreateMessage(notificationId, "+18173235812"),
            CancellationToken.None);

        // Assert
        _voiceCallSender.Verify(x => x.StartCallAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_MarksFailedWithoutRetry_WhenNoValidPhoneNumbers()
    {
        // Arrange
        var notificationId = Guid.NewGuid();

        var function = CreateFunction();

        // Act
        await function.Run(
            CreateMessage(notificationId, "not-a-number,12345"),
            CancellationToken.None);

        // Assert
        _voiceCallSender.Verify(x => x.StartCallAsync(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _auditStore.Verify(x => x.UpdateStatusAsync(
                notificationId,
                "Telegram",
                "Failed",
                null,
                "InvalidRecipients",
                It.IsAny<string>(),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_MarksSentWithPartialFailure_WhenSomeCallsFail()
    {
        // Arrange
        var notificationId = Guid.NewGuid();

        _voiceCallSender
            .Setup(x => x.StartCallAsync(
                "+18173235812",
                notificationId,
                AudioBlob,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("call-1");

        _voiceCallSender
            .Setup(x => x.StartCallAsync(
                "+16823005360",
                notificationId,
                AudioBlob,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ACS error"));

        var function = CreateFunction();

        // Act
        await function.Run(
            CreateMessage(notificationId, "+18173235812,+16823005360"),
            CancellationToken.None);

        // Assert
        _auditStore.Verify(x => x.UpdateStatusAsync(
                notificationId,
                "Telegram",
                "Sent",
                null,
                "PartialFailure",
                "+16823005360: ACS error",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_MarksFailedAndThrows_WhenAllCallsFail()
    {
        // Arrange
        var notificationId = Guid.NewGuid();

        _voiceCallSender
            .Setup(x => x.StartCallAsync(
                It.IsAny<string>(),
                notificationId,
                AudioBlob,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ACS error"));

        var function = CreateFunction();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            function.Run(
                CreateMessage(notificationId, "+18173235812"),
                CancellationToken.None));

        _auditStore.Verify(x => x.UpdateStatusAsync(
                notificationId,
                "Telegram",
                "Failed",
                null,
                null,
                It.Is<string>(m => m.Contains("ACS error")),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_MarksFailedWithoutRetry_WhenCategoryHasNoPlayableAudio()
    {
        var notificationId = Guid.NewGuid();

        _categories
            .Setup(x => x.ResolveAudioBlobAsync(CategoryId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AudioCategoryException("Category \"Support\" has no linked audio."));

        await CreateFunction().Run(CreateMessage(notificationId, "+18173235812"), CancellationToken.None);

        _voiceCallSender.Verify(x => x.StartCallAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _auditStore.Verify(x => x.UpdateStatusAsync(
                notificationId,
                "Telegram",
                "Failed",
                null,
                "AudioUnavailable",
                "Category \"Support\" has no linked audio.",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_MarksFailedWithoutRetry_WhenMessageHasNoCategory()
    {
        var notificationId = Guid.NewGuid();

        await CreateFunction().Run(CreateMessage(notificationId, (int?)null, "+18173235812"), CancellationToken.None);

        _categories.VerifyNoOtherCalls();
        _auditStore.Verify(x => x.UpdateStatusAsync(
                notificationId,
                "Telegram",
                "Failed",
                null,
                "AudioUnavailable",
                "The notification has no audio category.",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
