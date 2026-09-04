using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Enums;
using NotificationService.Functions.Functions;

namespace NotificationService.Functions.Tests.Functions;

public class NotificationIngressFunctionTests
{
    private readonly Mock<IBlobStorage> _blobStorage = new();
    private readonly Mock<IAuditStore> _auditStore = new();
    private readonly Mock<INotificationPublisher> _publisher = new();
    private readonly Mock<ILogger<NotificationIngressFunction>> _logger = new();

    private NotificationIngressFunction CreateFunction()
    {
        return new NotificationIngressFunction(
            _blobStorage.Object,
            _auditStore.Object,
            _publisher.Object,
            _logger.Object);
    }

    private static DefaultHttpContext CreateHttpContext(
        string? contentType = "multipart/form-data",
        Dictionary<string, string[]>? fields = null,
        params IFormFile[] files)
    {
        var context = new DefaultHttpContext();

        var formFields = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>();

        if (fields is not null)
        {
            foreach (var field in fields)
            {
                formFields[field.Key] =
                    new Microsoft.Extensions.Primitives.StringValues(field.Value);
            }
        }

        var form = new FormCollection(
            formFields,
            new FormFileCollection());

        context.Request.ContentType = contentType;

        context.Request.Form = form;

        return context;
    }

    private static DefaultHttpContext CreateMultipartHttpContext(
        Dictionary<string, string[]> fields,
        params IFormFile[] files)
    {
        var context = new DefaultHttpContext();

        var formFields =
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>();

        foreach (var field in fields)
        {
            formFields[field.Key] =
                new Microsoft.Extensions.Primitives.StringValues(field.Value);
        }

        var formFiles = new FormFileCollection();

        foreach (var file in files)
        {
            formFiles.Add(file);
        }

        context.Request.ContentType = "multipart/form-data; boundary=test";

        context.Request.Form = new FormCollection(
            formFields,
            formFiles);

        return context;
    }

    private static IFormFile CreateFile(
        string fileName,
        string content,
        string contentType = "text/plain")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);

        var stream = new MemoryStream(bytes);

        return new FormFile(
            stream,
            0,
            bytes.Length,
            "file",
            fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static IFormFile CreateEmptyFile(
        string fileName,
        string contentType = "text/plain")
    {
        var stream = new MemoryStream();

        return new FormFile(
            stream,
            0,
            0,
            "file",
            fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenRequestIsNotMultipart()
    {
        // Arrange
        var context = new DefaultHttpContext();

        context.Request.ContentType = "application/json";

        var function = CreateFunction();

        // Act
        var result = await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            badRequest.StatusCode);

        _blobStorage.Verify(x =>
            x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _auditStore.Verify(x =>
            x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _publisher.Verify(x =>
            x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenChannelIsInvalid()
    {
        // Arrange
        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["InvalidChannel"],
                ["recipient"] = ["test@example.com"],
                ["text"] = ["Hello"]
            });

        var function = CreateFunction();

        // Act
        var result = await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            badRequest.StatusCode);

        _auditStore.Verify(x =>
            x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _publisher.Verify(x =>
            x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenNoRecipientsAreProvided()
    {
        // Arrange
        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["", "   "],
                ["text"] = ["Hello"]
            });

        var function = CreateFunction();

        // Act
        var result = await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            badRequest.StatusCode);

        _publisher.Verify(x =>
            x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenTextIsMissing()
    {
        // Arrange
        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["test@example.com"],
                ["text"] = [""]
            });

        var function = CreateFunction();

        // Act
        var result = await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            badRequest.StatusCode);

        _auditStore.Verify(x =>
            x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _publisher.Verify(x =>
            x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenTextIsWhitespace()
    {
        // Arrange
        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["test@example.com"],
                ["text"] = ["   "]
            });

        var function = CreateFunction();

        // Act
        var result = await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status400BadRequest,
            badRequest.StatusCode);
    }

    [Fact]
    public async Task Run_AcceptsNotification_WhenRequestIsValid()
    {
        // Arrange
        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["test1@example.com"],
                ["text"] = ["Hello"],
                ["subject"] = ["Test subject"]
            });

        _auditStore
            .Setup(x => x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test1@example.com",
                "",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _publisher
            .Setup(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var function = CreateFunction();

        // Act
        var result = await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        var accepted = Assert.IsType<AcceptedResult>(result);

        Assert.Equal(
            StatusCodes.Status202Accepted,
            accepted.StatusCode);

        Assert.NotNull(accepted.Location);

        var value = accepted.Value!;

        var statusProperty = value.GetType().GetProperty("status");
        var notificationIdProperty = value.GetType().GetProperty("notificationId");

        var status = statusProperty!.GetValue(value)?.ToString();
        var notificationId = (Guid)notificationIdProperty!.GetValue(value)!;

        Assert.Equal("Accepted", status);
        Assert.NotEqual(Guid.Empty, notificationId);

        _auditStore.Verify(x =>
            x.CreateAcceptedAsync(
                notificationId,
                "Email",
                "test1@example.com",
                "",
                It.IsAny<CancellationToken>()),
            Times.Once);

        _publisher.Verify(x =>
            x.PublishAsync(
                It.Is<NotificationMessage>(m =>
                    m.NotificationId == notificationId &&
                    m.Channel == NotificationChannel.Email &&
                    m.Recipients.Count == 1 &&
                    m.Recipients[0] == "test1@example.com" &&
                    m.Subject == "Test subject" &&
                    m.Text == "Hello" &&
                    !m.Attachments.Any()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
    [Fact]
    public async Task Run_NormalizesRecipients_WhenRecipientsContainWhitespaceAndDuplicates()
    {
        // Arrange
        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] =
                [
                    " test@example.com ",
                    "TEST@example.com",
                    "",
                    "   ",
                    " second@example.com "
                ],
                ["text"] = ["Hello"]
            });

        _auditStore
            .Setup(x => x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test@example.com,second@example.com",
                "",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _publisher
            .Setup(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var function = CreateFunction();

        // Act
        await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        _auditStore.Verify(x =>
            x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test@example.com,second@example.com",
                "",
                It.IsAny<CancellationToken>()),
            Times.Once);

        _publisher.Verify(x =>
            x.PublishAsync(
                It.Is<NotificationMessage>(m =>
                    m.Recipients.SequenceEqual(
                        new[]
                        {
                            "test@example.com",
                            "second@example.com"
                        })),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_SetsSubjectToNull_WhenSubjectIsEmpty()
    {
        // Arrange
        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["test@example.com"],
                ["subject"] = ["   "],
                ["text"] = ["Hello"]
            });

        _auditStore
            .Setup(x => x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test@example.com",
                "",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _publisher
            .Setup(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var function = CreateFunction();

        // Act
        await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        _publisher.Verify(x =>
            x.PublishAsync(
                It.Is<NotificationMessage>(m =>
                    m.Subject == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_UploadsAttachments_WhenFilesAreProvided()
    {
        // Arrange
        var file1 = CreateFile(
            "file1.pdf",
            "PDF content",
            "application/pdf");

        var file2 = CreateFile(
            "file2.txt",
            "Text content",
            "text/plain");

        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["test@example.com"],
                ["text"] = ["Hello"]
            },
            file1,
            file2);

        var blobName1 = "notifications/file1.pdf";
        var blobName2 = "notifications/file2.txt";

        _blobStorage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                "file1.pdf",
                "application/pdf",
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobName1);

        _blobStorage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                "file2.txt",
                "text/plain",
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobName2);

        _auditStore
            .Setup(x => x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test@example.com",
                $"{blobName1},{blobName2}",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _publisher
            .Setup(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var function = CreateFunction();

        // Act
        var result = await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        Assert.IsType<AcceptedResult>(result);

        _blobStorage.Verify(x =>
            x.UploadAsync(
                It.IsAny<Stream>(),
                "file1.pdf",
                "application/pdf",
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _blobStorage.Verify(x =>
            x.UploadAsync(
                It.IsAny<Stream>(),
                "file2.txt",
                "text/plain",
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _auditStore.Verify(x =>
            x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test@example.com",
                $"{blobName1},{blobName2}",
                It.IsAny<CancellationToken>()),
            Times.Once);

        _publisher.Verify(x =>
            x.PublishAsync(
                It.Is<NotificationMessage>(m =>
                    m.Attachments.Count == 2 &&
                    m.Attachments.Any(a =>
                        a.BlobName == blobName1 &&
                        a.FileName == "file1.pdf" &&
                        a.ContentType == "application/pdf") &&
                    m.Attachments.Any(a =>
                        a.BlobName == blobName2 &&
                        a.FileName == "file2.txt" &&
                        a.ContentType == "text/plain")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_SkipsEmptyFiles()
    {
        // Arrange
        var emptyFile = CreateEmptyFile("empty.txt");

        var validFile = CreateFile(
            "valid.txt",
            "Hello",
            "text/plain");

        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["test@example.com"],
                ["text"] = ["Hello"]
            },
            emptyFile,
            validFile);

        var blobName = "notifications/valid.txt";

        _blobStorage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                "valid.txt",
                "text/plain",
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobName);

        _auditStore
            .Setup(x => x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test@example.com",
                blobName,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _publisher
            .Setup(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var function = CreateFunction();

        // Act
        await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        _blobStorage.Verify(x =>
            x.UploadAsync(
                It.IsAny<Stream>(),
                "empty.txt",
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _blobStorage.Verify(x =>
            x.UploadAsync(
                It.IsAny<Stream>(),
                "valid.txt",
                "text/plain",
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_ReturnsInternalServerError_WhenBlobUploadFails()
    {
        // Arrange
        var file = CreateFile(
            "test.txt",
            "Hello",
            "text/plain");

        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["test@example.com"],
                ["text"] = ["Hello"]
            },
            file);

        _blobStorage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                "test.txt",
                "text/plain",
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Upload failed"));

        var function = CreateFunction();

        // Act
        var result = await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            objectResult.StatusCode);

        _auditStore.Verify(x =>
            x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _publisher.Verify(x =>
            x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _blobStorage.Verify(x =>
            x.DeleteAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_DeletesUploadedBlobs_WhenAuditCreationFails()
    {
        // Arrange
        var file = CreateFile(
            "test.txt",
            "Hello",
            "text/plain");

        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["test@example.com"],
                ["text"] = ["Hello"]
            },
            file);

        var blobName = "notifications/test.txt";

        _blobStorage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                "test.txt",
                "text/plain",
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobName);

        _auditStore
            .Setup(x => x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test@example.com",
                blobName,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Audit failed"));

        var function = CreateFunction();

        // Act
        var result = await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            objectResult.StatusCode);

        _blobStorage.Verify(x =>
            x.DeleteAsync(
                blobName,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _publisher.Verify(x =>
            x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_DeletesUploadedBlobs_WhenPublishFails()
    {
        // Arrange
        var file = CreateFile(
            "test.txt",
            "Hello",
            "text/plain");

        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["test@example.com"],
                ["text"] = ["Hello"]
            },
            file);

        var blobName = "notifications/test.txt";

        _blobStorage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                "test.txt",
                "text/plain",
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobName);

        _auditStore
            .Setup(x => x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test@example.com",
                blobName,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _publisher
            .Setup(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Publish failed"));

        var function = CreateFunction();

        // Act
        var result = await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            objectResult.StatusCode);

        _blobStorage.Verify(x =>
            x.DeleteAsync(
                blobName,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_ReturnsInternalServerError_EvenWhenBlobCleanupFails()
    {
        // Arrange
        var file = CreateFile(
            "test.txt",
            "Hello",
            "text/plain");

        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["test@example.com"],
                ["text"] = ["Hello"]
            },
            file);

        var blobName = "notifications/test.txt";

        _blobStorage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                "test.txt",
                "text/plain",
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobName);

        _auditStore
            .Setup(x => x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test@example.com",
                blobName,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _publisher
            .Setup(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Publish failed"));

        _blobStorage
            .Setup(x => x.DeleteAsync(
                blobName,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Cleanup failed"));

        var function = CreateFunction();

        // Act
        var result = await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            objectResult.StatusCode);

        _blobStorage.Verify(x =>
            x.DeleteAsync(
                blobName,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_DeletesAllUploadedBlobs_WhenLaterOperationFails()
    {
        // Arrange
        var file1 = CreateFile(
            "file1.txt",
            "File 1",
            "text/plain");

        var file2 = CreateFile(
            "file2.txt",
            "File 2",
            "text/plain");

        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["test@example.com"],
                ["text"] = ["Hello"]
            },
            file1,
            file2);

        var blobName1 = "notifications/file1.txt";
        var blobName2 = "notifications/file2.txt";

        _blobStorage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                "file1.txt",
                "text/plain",
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobName1);

        _blobStorage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                "file2.txt",
                "text/plain",
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobName2);

        _auditStore
            .Setup(x => x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test@example.com",
                $"{blobName1},{blobName2}",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _publisher
            .Setup(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Publish failed"));

        var function = CreateFunction();

        // Act
        var result = await function.Run(
            context.Request,
            CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            objectResult.StatusCode);

        _blobStorage.Verify(x =>
            x.DeleteAsync(
                blobName1,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _blobStorage.Verify(x =>
            x.DeleteAsync(
                blobName2,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Run_PropagatesCancellationToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        var context = CreateMultipartHttpContext(
            new Dictionary<string, string[]>
            {
                ["channel"] = ["Email"],
                ["recipient"] = ["test@example.com"],
                ["text"] = ["Hello"]
            });

        _auditStore
            .Setup(x => x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test@example.com",
                "",
                cts.Token))
            .Returns(Task.CompletedTask);

        _publisher
            .Setup(x => x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                cts.Token))
            .Returns(Task.CompletedTask);

        var function = CreateFunction();

        // Act
        await function.Run(
            context.Request,
            cts.Token);

        // Assert
        _auditStore.Verify(x =>
            x.CreateAcceptedAsync(
                It.IsAny<Guid>(),
                "Email",
                "test@example.com",
                "",
                cts.Token),
            Times.Once);

        _publisher.Verify(x =>
            x.PublishAsync(
                It.IsAny<NotificationMessage>(),
                cts.Token),
            Times.Once);
    }

    private sealed class NotificationAcceptedResponse
    {
        public Guid NotificationId { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}

