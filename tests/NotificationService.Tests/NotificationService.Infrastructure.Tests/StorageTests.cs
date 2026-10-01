using Azure;
using Azure.Data.Tables;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using Moq;
using NotificationService.Application.Models;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Configuration;
using NotificationService.Infrastructure.Messaging;
using NotificationService.Infrastructure.Storage;

namespace NotificationService.Infrastructure.Tests;

public class AzureBlobStorageTests
{
    private readonly Mock<BlobContainerClient> _container = new();
    private readonly Mock<BlobClient> _blob = new();

    public AzureBlobStorageTests()
    {
        _container
            .Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Returns(_blob.Object);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_Throws_WhenConnectionStringIsMissing(string connectionString)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new AzureBlobStorage(Options.Create(new BlobStorageOptions { ConnectionString = connectionString })));
    }

    [Fact]
    public void Constructor_CreatesContainerClient_WhenConfigured()
    {
        var storage = new AzureBlobStorage(
            Options.Create(new BlobStorageOptions { ConnectionString = "UseDevelopmentStorage=true" }));

        Assert.NotNull(storage);
    }

    [Fact]
    public async Task UploadAsync_EnsuresContainer_AndUploadsWithContentType()
    {
        var notificationId = Guid.NewGuid();
        BlobUploadOptions? uploadOptions = null;

        _blob
            .Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, BlobUploadOptions, CancellationToken>((_, o, _) => uploadOptions = o)
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        var name = await new AzureBlobStorage(_container.Object).UploadAsync(
            new MemoryStream([1, 2, 3]),
            "../folder/report.pdf",
            "application/pdf",
            notificationId,
            CancellationToken.None);

        Assert.Equal($"notifications/{notificationId}/report.pdf", name);
        Assert.Equal("application/pdf", uploadOptions!.HttpHeaders.ContentType);
        _container.Verify(x => x.GetBlobClient(name), Times.Once);
    }

    [Fact]
    public async Task UploadBlobAsync_EnsuresContainer_AndUploadsToExactName()
    {
        BlobUploadOptions? uploadOptions = null;

        _blob
            .Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, BlobUploadOptions, CancellationToken>((_, o, _) => uploadOptions = o)
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        await new AzureBlobStorage(_container.Object).UploadBlobAsync(
            new MemoryStream([1, 2, 3]),
            "tts/2026/09/30/id.wav",
            "audio/wav",
            CancellationToken.None);

        Assert.Equal("audio/wav", uploadOptions!.HttpHeaders.ContentType);
        _container.Verify(x => x.GetBlobClient("tts/2026/09/30/id.wav"), Times.Once);
        _container.Verify(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsBlobStream()
    {
        var stream = new MemoryStream();

        _blob
            .Setup(x => x.OpenReadAsync(It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream);

        var result = await new AzureBlobStorage(_container.Object).OpenReadAsync("blob", CancellationToken.None);

        Assert.Same(stream, result);
    }

    [Fact]
    public async Task DeleteAsync_DeletesBlobIfExists()
    {
        await new AzureBlobStorage(_container.Object).DeleteAsync("blob", CancellationToken.None);

        _blob.Verify(x => x.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void GetReadUri_Throws_WhenSasCannotBeGenerated()
    {
        _blob.SetupGet(x => x.CanGenerateSasUri).Returns(false);

        Assert.Throws<InvalidOperationException>(() =>
            new AzureBlobStorage(_container.Object).GetReadUri("blob", TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void GetReadUri_ReturnsReadOnlySasUrl()
    {
        var container = new BlobContainerClient("UseDevelopmentStorage=true", "notifications");

        var uri = new AzureBlobStorage(container).GetReadUri("Audio/a.mp3", TimeSpan.FromMinutes(5));

        Assert.EndsWith("/notifications/Audio/a.mp3", uri.GetLeftPart(UriPartial.Path));
        Assert.Contains("sp=r", uri.Query);
        Assert.Contains("sig=", uri.Query);
    }
}

public class AzureTableAuditStoreTests
{
    private readonly Mock<TableClient> _table = new();

    private void SetupEntity(NotificationAuditEntity entity) =>
        _table
            .Setup(x => x.GetEntityAsync<NotificationAuditEntity>(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(entity, Mock.Of<Response>()));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_Throws_WhenConnectionStringIsMissing(string connectionString)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new AzureTableAuditStore(Options.Create(new AuditStorageOptions { ConnectionString = connectionString })));
    }

    [Fact]
    public void Constructor_CreatesTableClient_WhenConfigured()
    {
        var store = new AzureTableAuditStore(
            Options.Create(new AuditStorageOptions { ConnectionString = "UseDevelopmentStorage=true" }));

        Assert.NotNull(store);
    }

    [Fact]
    public async Task CreateAcceptedAsync_AddsAcceptedEntity()
    {
        var id = Guid.NewGuid();
        NotificationAuditEntity? added = null;

        _table
            .Setup(x => x.AddEntityAsync(It.IsAny<NotificationAuditEntity>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationAuditEntity, CancellationToken>((e, _) => added = e)
            .ReturnsAsync(Mock.Of<Response>());

        await new AzureTableAuditStore(_table.Object).CreateAcceptedAsync(
            id, "Email", "a@b.c", "blob", CancellationToken.None);

        _table.Verify(x => x.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Email", added!.PartitionKey);
        Assert.Equal(id.ToString(), added.RowKey);
        Assert.Equal("Accepted", added.Status);
        Assert.Equal("a@b.c", added.Recipient);
        Assert.Equal("blob", added.BlobName);
        Assert.Equal(0, added.RetryCount);
    }

    [Fact]
    public async Task UpdateStatusAsync_UpdatesAllProvidedFields()
    {
        var entity = new NotificationAuditEntity { Status = "Accepted", ETag = new ETag("1") };
        SetupEntity(entity);

        await new AzureTableAuditStore(_table.Object).UpdateStatusAsync(
            Guid.NewGuid(), "Email", "Failed", "provider", "code", "message", 3, CancellationToken.None);

        Assert.Equal("Failed", entity.Status);
        Assert.Equal("provider", entity.ProviderMessageId);
        Assert.Equal("code", entity.ErrorCode);
        Assert.Equal("message", entity.ErrorMessage);
        Assert.Equal(3, entity.RetryCount);

        _table.Verify(x => x.UpdateEntityAsync(entity, new ETag("1"), TableUpdateMode.Replace, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStatusAsync_KeepsExistingFields_WhenOptionalValuesAreNull()
    {
        var entity = new NotificationAuditEntity
        {
            ProviderMessageId = "p",
            ErrorCode = "c",
            ErrorMessage = "m",
            RetryCount = 1
        };
        SetupEntity(entity);

        await new AzureTableAuditStore(_table.Object).UpdateStatusAsync(Guid.NewGuid(), "Email", "Sent");

        Assert.Equal("Sent", entity.Status);
        Assert.Equal("p", entity.ProviderMessageId);
        Assert.Equal("c", entity.ErrorCode);
        Assert.Equal("m", entity.ErrorMessage);
        Assert.Equal(1, entity.RetryCount);
    }

    [Theory]
    [InlineData("Sent", true)]
    [InlineData("Processing", false)]
    public async Task HasBeenProcessedAsync_ReturnsTrueOnlyWhenSent(string status, bool expected)
    {
        SetupEntity(new NotificationAuditEntity { Status = status });

        Assert.Equal(
            expected,
            await new AzureTableAuditStore(_table.Object).HasBeenProcessedAsync(Guid.NewGuid(), "Email", CancellationToken.None));
    }

    [Fact]
    public async Task HasBeenProcessedAsync_ReturnsFalse_WhenRecordIsMissing()
    {
        _table
            .Setup(x => x.GetEntityAsync<NotificationAuditEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        Assert.False(await new AzureTableAuditStore(_table.Object).HasBeenProcessedAsync(Guid.NewGuid(), "Email", CancellationToken.None));
    }

    [Fact]
    public async Task HasBeenProcessedAsync_Rethrows_OtherStorageErrors()
    {
        _table
            .Setup(x => x.GetEntityAsync<NotificationAuditEntity>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "Server error"));

        await Assert.ThrowsAsync<RequestFailedException>(() =>
            new AzureTableAuditStore(_table.Object).HasBeenProcessedAsync(Guid.NewGuid(), "Email", CancellationToken.None));
    }
}

public class ServiceBusNotificationPublisherTests
{
    private readonly Mock<ServiceBusClient> _client = new();
    private readonly Mock<ServiceBusSender> _sender = new();

    public ServiceBusNotificationPublisherTests()
    {
        _client.Setup(x => x.CreateSender(It.IsAny<string>())).Returns(_sender.Object);
    }

    private static NotificationMessage Message(NotificationChannel channel) =>
        new()
        {
            NotificationId = Guid.NewGuid(),
            Channel = channel,
            Recipients = ["a"],
            Text = "text"
        };

    [Fact]
    public async Task PublishAsync_SendsJsonToQueueNamedAfterChannel()
    {
        var message = Message(NotificationChannel.Telegram);
        ServiceBusMessage? sent = null;

        _sender
            .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ServiceBusMessage, CancellationToken>((m, _) => sent = m)
            .Returns(Task.CompletedTask);

        await new ServiceBusNotificationPublisher(_client.Object).PublishAsync(message, CancellationToken.None);

        _client.Verify(x => x.CreateSender("telegram"), Times.Once);
        Assert.Equal("application/json", sent!.ContentType);
        Assert.Equal(message.NotificationId.ToString(), sent.MessageId);
        Assert.Contains(message.NotificationId.ToString(), sent.Body.ToString());
        _sender.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_DisposesSender_WhenSendFails()
    {
        _sender
            .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bus down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ServiceBusNotificationPublisher(_client.Object).PublishAsync(Message(NotificationChannel.Email), CancellationToken.None));

        _sender.Verify(x => x.DisposeAsync(), Times.Once);
    }
}
