using Microsoft.AspNetCore.Http;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Models;

public sealed class NotificationRequest
{
    public NotificationChannel Channel { get; set; }

    public string Recipient { get; set; } = string.Empty;

    public string? Subject { get; set; }

    public string Text { get; set; } = string.Empty;

    public IFormFile? File { get; set; }
}