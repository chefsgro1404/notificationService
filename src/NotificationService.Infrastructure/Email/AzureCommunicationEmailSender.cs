using Azure.Communication.Email;
using Microsoft.Extensions.Options;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Email;

public sealed class AzureCommunicationEmailSender
    : IEmailSender
{
    private readonly EmailClient _client;
    private readonly AzureCommunicationServicesOptions _options;

    public AzureCommunicationEmailSender(
        IOptions<AzureCommunicationServicesOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(
                _options.ConnectionString))  
        {
            throw new InvalidOperationException(
                "AzureCommunicationServices:ConnectionString " +
                "is required.");
        }

        if (string.IsNullOrWhiteSpace(
                _options.SenderAddress))
        {
            throw new InvalidOperationException(
                "AzureCommunicationServices:SenderAddress " +
                "is required.");
        }

        _client = new EmailClient(
            _options.ConnectionString);
    }

    /// <inheritdoc />
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

        var content =
            new EmailContent(
                subject ?? string.Empty)
            {
                Html = text,
                PlainText = text
            };

        var emailRecipients =
            new EmailRecipients();

        foreach (var recipient in recipientList)
        {
            emailRecipients.To.Add(
                new EmailAddress(recipient));
        }

        var email =
            new EmailMessage(
                _options.SenderAddress,
                emailRecipients,
                content);

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

                var azureAttachment =
                    new Azure.Communication.Email.EmailAttachment(
                        attachment.FileName,
                        attachment.ContentType ??
                        "application/octet-stream",
                        BinaryData.FromBytes(
                            memoryStream.ToArray()));

                email.Attachments.Add(
                    azureAttachment);
            }
        }

        var operation =
            await _client.SendAsync(
                Azure.WaitUntil.Started,
                email,
                cancellationToken);

        return operation.Id;
    }
}