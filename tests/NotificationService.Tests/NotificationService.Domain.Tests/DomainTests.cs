using Azure;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Domain.Tests;

public class NotificationTests
{
    private static Notification Create() =>
        new(
            Guid.NewGuid(),
            NotificationChannel.Email,
            "user@example.com",
            "Hello",
            "Subject",
            "blob",
            "file.txt",
            "text/plain");

    [Fact]
    public void Constructor_SetsValues_AndStatusAccepted()
    {
        var id = Guid.NewGuid();

        var notification = new Notification(
            id,
            NotificationChannel.Telegram,
            "+18173235812",
            "Text",
            "Subject",
            "blob",
            "file.txt",
            "text/plain");

        Assert.Equal(id, notification.NotificationId);
        Assert.Equal(NotificationChannel.Telegram, notification.Channel);
        Assert.Equal("+18173235812", notification.Recipient);
        Assert.Equal("Text", notification.Text);
        Assert.Equal("Subject", notification.Subject);
        Assert.Equal("blob", notification.BlobName);
        Assert.Equal("file.txt", notification.FileName);
        Assert.Equal("text/plain", notification.ContentType);
        Assert.Equal(NotificationStatus.Accepted, notification.Status);
        Assert.True(notification.CreatedAtUtc <= DateTime.UtcNow);
    }

    [Fact]
    public void Constructor_Throws_WhenIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new Notification(Guid.Empty, NotificationChannel.Email, "a", "b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_Throws_WhenRecipientIsBlank(string recipient)
    {
        Assert.Throws<ArgumentException>(() =>
            new Notification(Guid.NewGuid(), NotificationChannel.Email, recipient, "b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_Throws_WhenTextIsBlank(string text)
    {
        Assert.Throws<ArgumentException>(() =>
            new Notification(Guid.NewGuid(), NotificationChannel.Email, "a", text));
    }

    [Fact]
    public void PrivateConstructor_InitializesEmptyStrings()
    {
        var notification = (Notification)Activator.CreateInstance(
            typeof(Notification),
            nonPublic: true)!;

        Assert.Equal(string.Empty, notification.Recipient);
        Assert.Equal(string.Empty, notification.Text);
    }

    [Fact]
    public void MarkMethods_ChangeStatus()
    {
        var notification = Create();

        notification.MarkQueued();
        Assert.Equal(NotificationStatus.Queued, notification.Status);

        notification.MarkProcessing();
        Assert.Equal(NotificationStatus.Processing, notification.Status);

        notification.MarkRetrying();
        Assert.Equal(NotificationStatus.Retrying, notification.Status);

        notification.MarkSent();
        Assert.Equal(NotificationStatus.Sent, notification.Status);

        notification.MarkFailed();
        Assert.Equal(NotificationStatus.Failed, notification.Status);
    }
}

public class NotificationAuditEntityTests
{
    [Fact]
    public void Properties_RoundTrip()
    {
        var now = DateTime.UtcNow;
        var timestamp = DateTimeOffset.UtcNow;

        var entity = new NotificationAuditEntity
        {
            PartitionKey = "Email",
            RowKey = "id",
            Timestamp = timestamp,
            ETag = new ETag("etag"),
            Channel = "Email",
            Recipient = "a@b.c",
            Status = "Sent",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ProviderMessageId = "provider",
            ErrorCode = "code",
            ErrorMessage = "message",
            BlobName = "blob",
            RetryCount = 2
        };

        Assert.Equal("Email", entity.PartitionKey);
        Assert.Equal("id", entity.RowKey);
        Assert.Equal(timestamp, entity.Timestamp);
        Assert.Equal(new ETag("etag"), entity.ETag);
        Assert.Equal("Email", entity.Channel);
        Assert.Equal("a@b.c", entity.Recipient);
        Assert.Equal("Sent", entity.Status);
        Assert.Equal(now, entity.CreatedAtUtc);
        Assert.Equal(now, entity.UpdatedAtUtc);
        Assert.Equal("provider", entity.ProviderMessageId);
        Assert.Equal("code", entity.ErrorCode);
        Assert.Equal("message", entity.ErrorMessage);
        Assert.Equal("blob", entity.BlobName);
        Assert.Equal(2, entity.RetryCount);
    }

    [Fact]
    public void Defaults_AreEmpty()
    {
        var entity = new NotificationAuditEntity();

        Assert.Equal(string.Empty, entity.PartitionKey);
        Assert.Equal(string.Empty, entity.RowKey);
        Assert.Equal(string.Empty, entity.Channel);
        Assert.Equal(string.Empty, entity.Recipient);
        Assert.Equal(string.Empty, entity.Status);
    }
}
