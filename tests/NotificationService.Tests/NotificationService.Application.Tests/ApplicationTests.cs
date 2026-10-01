using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NotificationService.Application.DependencyInjection;
using NotificationService.Application.Models;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Tests;

public class ApplicationServiceExtensionsTests
{
    [Fact]
    public void AddApplicationServices_ReturnsSameCollection()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddApplicationServices());
    }
}

public class NotificationRequestTests
{
    [Fact]
    public void Properties_RoundTrip()
    {
        var file = Mock.Of<IFormFile>();

        var request = new NotificationRequest
        {
            Channel = NotificationChannel.Email,
            Recipient = "a@b.c",
            Subject = "Subject",
            Text = "Text",
            File = file
        };

        Assert.Equal(NotificationChannel.Email, request.Channel);
        Assert.Equal("a@b.c", request.Recipient);
        Assert.Equal("Subject", request.Subject);
        Assert.Equal("Text", request.Text);
        Assert.Same(file, request.File);
    }

    [Fact]
    public void Defaults_AreEmpty()
    {
        var request = new NotificationRequest();

        Assert.Equal(string.Empty, request.Recipient);
        Assert.Equal(string.Empty, request.Text);
        Assert.Null(request.Subject);
        Assert.Null(request.File);
    }
}
