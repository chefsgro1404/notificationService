using Azure;
using Azure.Communication.Email;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using NotificationService.Application.Models;
using NotificationService.Infrastructure.Configuration;
using NotificationService.Infrastructure.Email;

namespace NotificationService.Infrastructure.Tests;

public class AzureCommunicationEmailSenderTests
{
    private const string FakeConnectionString =
        "endpoint=https://test.communication.azure.com/;accesskey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private readonly Mock<EmailClient> _client = new();
    private EmailMessage? _sent;

    public AzureCommunicationEmailSenderTests()
    {
        var operation = new Mock<EmailSendOperation>();
        operation.SetupGet(x => x.Id).Returns("operation-id");

        _client
            .Setup(x => x.SendAsync(It.IsAny<WaitUntil>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<WaitUntil, EmailMessage, CancellationToken>((_, m, _) => _sent = m)
            .ReturnsAsync(operation.Object);
    }

    private AzureCommunicationEmailSender CreateSender() =>
        new(_client.Object, Options.Create(new AzureCommunicationServicesOptions
        {
            ConnectionString = FakeConnectionString,
            SenderAddress = "noreply@example.com"
        }));

    [Theory]
    [InlineData("", "noreply@example.com")]
    [InlineData(FakeConnectionString, "")]
    public void Constructor_Throws_WhenSettingsAreMissing(string connectionString, string sender)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new AzureCommunicationEmailSender(Options.Create(new AzureCommunicationServicesOptions
            {
                ConnectionString = connectionString,
                SenderAddress = sender
            })));
    }

    [Fact]
    public void Constructor_CreatesClient_WhenConfigured()
    {
        var sender = new AzureCommunicationEmailSender(Options.Create(new AzureCommunicationServicesOptions
        {
            ConnectionString = FakeConnectionString,
            SenderAddress = "noreply@example.com"
        }));

        Assert.NotNull(sender);
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNoRecipients()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateSender().SendAsync([" ", ""], "s", "t", null, CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_SendsDistinctRecipients_AndReturnsOperationId()
    {
        var id = await CreateSender().SendAsync(
            ["a@b.c", " A@B.C ", "d@e.f"], null, "<b>Hi</b>", null, CancellationToken.None);

        Assert.Equal("operation-id", id);
        Assert.Equal("noreply@example.com", _sent!.SenderAddress);
        Assert.Equal(2, _sent.Recipients.To.Count);
        Assert.Equal(string.Empty, _sent.Content.Subject);
        Assert.Equal("<b>Hi</b>", _sent.Content.Html);
        Assert.Equal("<b>Hi</b>", _sent.Content.PlainText);
        Assert.Empty(_sent.Attachments);
    }

    [Fact]
    public async Task SendAsync_AddsValidAttachments_AndSkipsInvalidOnes()
    {
        var attachments = new[]
        {
            new EmailAttachmentFile { Stream = null!, FileName = "no-stream.txt" },
            new EmailAttachmentFile { Stream = new MemoryStream([1]), FileName = " " },
            new EmailAttachmentFile { Stream = new MemoryStream([1, 2]), FileName = "a.bin" },
            new EmailAttachmentFile { Stream = new MemoryStream([3]), FileName = "b.pdf", ContentType = "application/pdf" }
        };

        await CreateSender().SendAsync(["a@b.c"], "Subject", "t", attachments, CancellationToken.None);

        Assert.Equal("Subject", _sent!.Content.Subject);
        Assert.Collection(
            _sent.Attachments,
            a =>
            {
                Assert.Equal("a.bin", a.Name);
                Assert.Equal("application/octet-stream", a.ContentType);
                Assert.Equal(new byte[] { 1, 2 }, a.Content.ToArray());
            },
            b =>
            {
                Assert.Equal("b.pdf", b.Name);
                Assert.Equal("application/pdf", b.ContentType);
            });
    }
}

public class Smtp4DevEmailSenderTests
{
    private readonly Mock<ISmtpClient> _smtp = new();
    private MimeMessage? _sent;

    public Smtp4DevEmailSenderTests()
    {
        _smtp
            .Setup(x => x.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>(), It.IsAny<ITransferProgress>()))
            .Callback<MimeMessage, CancellationToken, ITransferProgress>((m, _, _) => _sent = m)
            .ReturnsAsync("ok");
    }

    private Smtp4DevEmailSender CreateSender(SmtpOptions? smtp = null) =>
        new(
            Options.Create(new EmailOptions { From = "from@example.com", Smtp = smtp ?? new SmtpOptions() }),
            () => _smtp.Object);

    [Fact]
    public void Constructor_Throws_WhenFromIsMissing()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new Smtp4DevEmailSender(Options.Create(new EmailOptions { From = " " })));
    }

    [Fact]
    public void Constructor_Throws_WhenHostIsMissing()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new Smtp4DevEmailSender(Options.Create(new EmailOptions
            {
                From = "from@example.com",
                Smtp = new SmtpOptions { Host = " " }
            })));
    }

    [Fact]
    public void CreateSmtpClient_ReturnsMailKitClient()
    {
        using var client = Smtp4DevEmailSender.CreateSmtpClient();

        Assert.IsType<SmtpClient>(client);
    }

    [Fact]
    public void PublicConstructor_Succeeds_WhenConfigured()
    {
        Assert.NotNull(new Smtp4DevEmailSender(Options.Create(new EmailOptions { From = "from@example.com" })));
    }

    [Fact]
    public async Task SendAsync_Throws_WhenNoRecipients()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateSender().SendAsync([" "], "s", "t", null, CancellationToken.None));
    }

    [Fact]
    public async Task SendAsync_WithoutSslOrCredentials_ConnectsPlainAndSkipsAuth()
    {
        var id = await CreateSender().SendAsync(["a@b.c", "A@B.C"], null, "body", null, CancellationToken.None);

        Assert.Equal(_sent!.MessageId, id);
        Assert.Single(_sent.To);
        Assert.Equal(string.Empty, _sent.Subject);
        Assert.Equal("body", _sent.TextBody);
        Assert.Equal("body", _sent.HtmlBody);

        _smtp.Verify(x => x.ConnectAsync("localhost", 2525, SecureSocketOptions.None, It.IsAny<CancellationToken>()), Times.Once);
        _smtp.Verify(x => x.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _smtp.Verify(x => x.DisconnectAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_WithSslAndCredentials_UsesSslAndAuthenticates()
    {
        var smtp = new SmtpOptions { Host = "smtp.example.com", Port = 465, UseSsl = true, Username = "user", Password = "pass" };

        await CreateSender(smtp).SendAsync(["a@b.c"], "Subject", "body", null, CancellationToken.None);

        Assert.Equal("Subject", _sent!.Subject);
        _smtp.Verify(x => x.ConnectAsync("smtp.example.com", 465, SecureSocketOptions.SslOnConnect, It.IsAny<CancellationToken>()), Times.Once);
        _smtp.Verify(x => x.AuthenticateAsync("user", "pass", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_AddsValidAttachments_AndSkipsInvalidOnes()
    {
        var attachments = new[]
        {
            new EmailAttachmentFile { Stream = null!, FileName = "no-stream.txt" },
            new EmailAttachmentFile { Stream = new MemoryStream([1]), FileName = "" },
            new EmailAttachmentFile { Stream = new MemoryStream([1, 2]), FileName = "a.bin" },
            new EmailAttachmentFile { Stream = new MemoryStream([3]), FileName = "b.pdf", ContentType = "application/pdf" }
        };

        await CreateSender().SendAsync(["a@b.c"], "s", "t", attachments, CancellationToken.None);

        var names = _sent!.Attachments.OfType<MimePart>().Select(x => (x.FileName, x.ContentType.MimeType)).ToList();

        Assert.Equal(
            [("a.bin", "application/octet-stream"), ("b.pdf", "application/pdf")],
            names);
    }
}
