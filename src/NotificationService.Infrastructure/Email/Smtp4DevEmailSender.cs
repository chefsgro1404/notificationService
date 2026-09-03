using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Email;

public sealed class Smtp4DevEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public Smtp4DevEmailSender(
        IOptions<EmailOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.From))
        {
            throw new InvalidOperationException(
                "Email:From is required.");
        }

        if (string.IsNullOrWhiteSpace(
                _options.Smtp.Host))
        {
            throw new InvalidOperationException(
                "Email:Smtp:Host is required.");
        }
    }

    public async Task<string?> SendAsync(
    IEnumerable<string> recipients,
    string? subject,
    string text,
    IEnumerable<EmailAttachmentFile>? attachments,
    CancellationToken cancellationToken)
    {
        var recipientList = recipients
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (recipientList.Count == 0)
        {
            throw new ArgumentException(
                "At least one recipient is required.",
                nameof(recipients));
        }

        var message = new MimeMessage();

        message.From.Add(
            MailboxAddress.Parse(
                _options.From));

        foreach (var recipient in recipientList)
        {
            message.To.Add(
                MailboxAddress.Parse(recipient));
        }

        message.Subject =
            subject ?? string.Empty;

        var body =
            new BodyBuilder
            {
                TextBody = text
            };

        if (attachments is not null)
        {
            foreach (var attachment in attachments)
            {
                if (attachment.Stream is null ||
                    string.IsNullOrWhiteSpace(
                        attachment.FileName))
                {
                    continue;
                }

                await using var memoryStream =
                    new MemoryStream();

                await attachment.Stream.CopyToAsync(
                    memoryStream,
                    cancellationToken);

                body.Attachments.Add(
                    attachment.FileName,
                    memoryStream.ToArray(),
                    ContentType.Parse(
                        attachment.ContentType ??
                        "application/octet-stream"));
            }
        }

        message.Body =
            body.ToMessageBody();

        using var smtp =
            new SmtpClient();

        var security =
            _options.Smtp.UseSsl
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.None;

        await smtp.ConnectAsync(
            _options.Smtp.Host,
            _options.Smtp.Port,
            security,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(
                _options.Smtp.Username))
        {
            await smtp.AuthenticateAsync(
                _options.Smtp.Username,
                _options.Smtp.Password,
                cancellationToken);
        }

        await smtp.SendAsync(
            message,
            cancellationToken);

        await smtp.DisconnectAsync(
            true,
            cancellationToken);

        return message.MessageId;
    }
}