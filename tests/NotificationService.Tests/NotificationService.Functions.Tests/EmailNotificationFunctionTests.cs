using Microsoft.Extensions.Logging;
using Moq;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Enums;
using NotificationService.Functions.Functions;
using System.Text.Json;
namespace NotificationService.Functions.Tests;
public class EmailNotificationFunctionTests
{
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IBlobStorage> _blobStorage = new();
    private readonly Mock<IAuditStore> _auditStore = new();
    private readonly Mock<ILogger<EmailNotificationFunction>> _logger = new();

    private EmailNotificationFunction CreateFunction()
    {
        return new EmailNotificationFunction(
            _emailSender.Object,
            _blobStorage.Object,
            _auditStore.Object,
            _logger.Object);
    }

    [Fact]
    public async Task Run_SendsEmail_WhenNotificationIsValid()
    {
        // Arrange
        var notificationId = Guid.NewGuid();

        var notification = new NotificationMessage
        {
            NotificationId = notificationId,
            Channel = NotificationChannel.Email,
            Recipients =
            [
                "test1@example.com",
                "test2@example.com"
            ],
            Subject = "Test",
            Text = "Hello",
            Attachments = [],
            CreatedAtUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(notification);

        _auditStore
            .Setup(x => x.HasBeenProcessedAsync(
                notificationId,
                "Email",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _emailSender
            .Setup(x => x.SendAsync(
                It.IsAny<IEnumerable<string>>(),
                notification.Subject,
                notification.Text,
                It.IsAny<IEnumerable<EmailAttachmentFile>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("provider-message-id");

        var function = CreateFunction();

        // Act
        await function.Run(json, CancellationToken.None);

        // Assert
        _emailSender.Verify(x =>
            x.SendAsync(
                It.Is<IEnumerable<string>>(r =>
                    r.SequenceEqual(notification.Recipients)),
                notification.Subject,
                notification.Text,
                It.Is<IEnumerable<EmailAttachmentFile>>(a =>
                    !a.Any()),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Processing
        _auditStore.Verify(x =>
            x.UpdateStatusAsync(
                notificationId,
                "Email",
                "Processing",
                null,
                null,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Sent
        _auditStore.Verify(x =>
            x.UpdateStatusAsync(
                notificationId,
                "Email",
                "Sent",
                "provider-message-id",
                null,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_DoesNotSend_WhenAlreadyProcessed()
    {
        // Arrange
        var notificationId = Guid.NewGuid();

        var notification = new NotificationMessage
        {
            NotificationId = notificationId,
            Channel = NotificationChannel.Email,
            Recipients =
            [
                "test@example.com"
            ],
            Subject = "Test",
            Text = "Hello",
            Attachments = []
        };

        var json = JsonSerializer.Serialize(notification);

        _auditStore
            .Setup(x => x.HasBeenProcessedAsync(
                notificationId,
                "Email",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var function = CreateFunction();

        // Act
        await function.Run(json, CancellationToken.None);

        // Assert
        _emailSender.Verify(x =>
            x.SendAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<EmailAttachmentFile>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _auditStore.Verify(x =>
            x.UpdateStatusAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_MarksFailed_WhenEmailSendingFails()
    {
        // Arrange
        var notificationId = Guid.NewGuid();

        var notification = new NotificationMessage
        {
            NotificationId = notificationId,
            Channel = NotificationChannel.Email,
            Recipients =
            [
                "test@example.com"
            ],
            Subject = "Test",
            Text = "Hello",
            Attachments = []
        };

        var json = JsonSerializer.Serialize(notification);

        _auditStore
            .Setup(x => x.HasBeenProcessedAsync(
                notificationId,
                "Email",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _emailSender
            .Setup(x => x.SendAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<EmailAttachmentFile>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SMTP failed"));

        var function = CreateFunction();

        // Act
        var exception = await Assert.ThrowsAsync<Exception>(() =>
            function.Run(json, CancellationToken.None));

        // Assert
        Assert.Equal("SMTP failed", exception.Message);

        _auditStore.Verify(x =>
            x.UpdateStatusAsync(
                notificationId,
                "Email",
                "Processing",
                null,
                null,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _auditStore.Verify(x =>
            x.UpdateStatusAsync(
                notificationId,
                "Email",
                "Failed",
                null,
                null,
                "SMTP failed",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_Throws_WhenNotificationIsNull()
    {
        // Arrange
        var function = CreateFunction();

        // JSON "null" deserializes to null
        var json = "null";

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            function.Run(json, CancellationToken.None));

        // Assert
        Assert.Equal(
            "Invalid notification message.",
            exception.Message);

        _auditStore.Verify(x =>
            x.HasBeenProcessedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _emailSender.Verify(x =>
            x.SendAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<EmailAttachmentFile>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_Throws_WhenJsonIsInvalid()
    {
        // Arrange
        var function = CreateFunction();

        var json = "{ invalid json }";

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(() =>
            function.Run(json, CancellationToken.None));

        _auditStore.Verify(x =>
            x.HasBeenProcessedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _emailSender.Verify(x =>
            x.SendAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<EmailAttachmentFile>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_OpensAndSendsAttachments_WhenAttachmentsExist()
    {
        // Arrange
        var notificationId = Guid.NewGuid();

        var stream1 = new MemoryStream();
        var stream2 = new MemoryStream();

        var notification = new NotificationMessage
        {
            NotificationId = notificationId,
            Channel = NotificationChannel.Email,
            Recipients = ["test@example.com"],
            Subject = "Test",
            Text = "Hello",
            Attachments =
            [
                new NotificationAttachment
                {
                    BlobName = "documents/file1.pdf",
                    FileName = "file1.pdf",
                    ContentType = "application/pdf"
                },
                new NotificationAttachment
                {
                    BlobName = "documents/file2.txt",
                    FileName = "file2.txt",
                    ContentType = "text/plain"
                }
            ]
        };

        var json = JsonSerializer.Serialize(notification);

        _auditStore
            .Setup(x => x.HasBeenProcessedAsync(
                notificationId,
                "Email",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _blobStorage
            .Setup(x => x.OpenReadAsync(
                "documents/file1.pdf",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream1);

        _blobStorage
            .Setup(x => x.OpenReadAsync(
                "documents/file2.txt",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream2);

        _emailSender
            .Setup(x => x.SendAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<EmailAttachmentFile>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("provider-message-id");

        var function = CreateFunction();

        // Act
        await function.Run(json, CancellationToken.None);

        // Assert
        _blobStorage.Verify(x =>
            x.OpenReadAsync(
                "documents/file1.pdf",
                It.IsAny<CancellationToken>()),
            Times.Once);

        _blobStorage.Verify(x =>
            x.OpenReadAsync(
                "documents/file2.txt",
                It.IsAny<CancellationToken>()),
            Times.Once);

        _emailSender.Verify(x =>
            x.SendAsync(
                It.IsAny<IEnumerable<string>>(),
                notification.Subject,
                notification.Text,
                It.Is<IEnumerable<EmailAttachmentFile>>(attachments =>
                    attachments.Count() == 2 &&
                    attachments.Any(a =>
                        a.FileName == "file1.pdf" &&
                        a.ContentType == "application/pdf") &&
                    attachments.Any(a =>
                        a.FileName == "file2.txt" &&
                        a.ContentType == "text/plain")),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _auditStore.Verify(x =>
            x.UpdateStatusAsync(
                notificationId,
                "Email",
                "Sent",
                "provider-message-id",
                null,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // finally block should dispose them
        Assert.Throws<ObjectDisposedException>(() =>
            stream1.ReadByte());

        Assert.Throws<ObjectDisposedException>(() =>
            stream2.ReadByte());
    }

    [Fact]
    public async Task Run_MarksFailed_WhenBlobAttachmentFails()
    {
        // Arrange
        var notificationId = Guid.NewGuid();

        var notification = new NotificationMessage
        {
            NotificationId = notificationId,
            Channel = NotificationChannel.Email,
            Recipients = ["test@example.com"],
            Subject = "Test",
            Text = "Hello",
            Attachments =
            [
                new NotificationAttachment
                {
                    BlobName = "missing.pdf",
                    FileName = "missing.pdf",
                    ContentType = "application/pdf"
                }
            ]
        };

        var json = JsonSerializer.Serialize(notification);

        _auditStore
            .Setup(x => x.HasBeenProcessedAsync(
                notificationId,
                "Email",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _blobStorage
            .Setup(x => x.OpenReadAsync(
                "missing.pdf",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Blob not found"));

        var function = CreateFunction();

        // Act
        var exception = await Assert.ThrowsAsync<Exception>(() =>
            function.Run(json, CancellationToken.None));

        // Assert
        Assert.Equal("Blob not found", exception.Message);

        _emailSender.Verify(x =>
            x.SendAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<EmailAttachmentFile>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _auditStore.Verify(x =>
            x.UpdateStatusAsync(
                notificationId,
                "Email",
                "Processing",
                null,
                null,
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _auditStore.Verify(x =>
            x.UpdateStatusAsync(
                notificationId,
                "Email",
                "Failed",
                null,
                null,
                "Blob not found",
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_PropagatesCancellationToken()
    {
        // Arrange
        var notificationId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();

        var notification = new NotificationMessage
        {
            NotificationId = notificationId,
            Channel = NotificationChannel.Email,
            Recipients = ["test@example.com"],
            Subject = "Test",
            Text = "Hello",
            Attachments = []
        };

        var json = JsonSerializer.Serialize(notification);

        _auditStore
            .Setup(x => x.HasBeenProcessedAsync(
                notificationId,
                "Email",
                cts.Token))
            .ReturnsAsync(false);

        _emailSender
            .Setup(x => x.SendAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<EmailAttachmentFile>?>(),
                cts.Token))
            .ReturnsAsync("provider-message-id");

        var function = CreateFunction();

        // Act
        await function.Run(json, cts.Token);

        // Assert
        _auditStore.Verify(x =>
            x.HasBeenProcessedAsync(
                notificationId,
                "Email",
                cts.Token),
            Times.Once);

        _auditStore.Verify(x =>
            x.UpdateStatusAsync(
                notificationId,
                "Email",
                "Processing",
                null,
                null,
                null,
                null,
                cts.Token),
            Times.Once);

        _emailSender.Verify(x =>
            x.SendAsync(
                It.IsAny<IEnumerable<string>>(),
                notification.Subject,
                notification.Text,
                It.IsAny<IEnumerable<EmailAttachmentFile>?>(),
                cts.Token),
            Times.Once);

        _auditStore.Verify(x =>
            x.UpdateStatusAsync(
                notificationId,
                "Email",
                "Sent",
                "provider-message-id",
                null,
                null,
                null,
                cts.Token),
            Times.Once);
    }
}

