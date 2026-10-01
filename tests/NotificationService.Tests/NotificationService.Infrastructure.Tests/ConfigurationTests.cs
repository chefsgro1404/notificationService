using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Application.Interfaces;
using NotificationService.Infrastructure.Configuration;
using NotificationService.Infrastructure.DependencyInjection;
using NotificationService.Infrastructure.Email;
using NotificationService.Infrastructure.Messaging;
using NotificationService.Infrastructure.Sms;
using NotificationService.Infrastructure.Telegram;
using NotificationService.Infrastructure.Voice;

namespace NotificationService.Infrastructure.Tests;

public class OptionsTests
{
    [Fact]
    public void Defaults_AreApplied()
    {
        var audit = new AuditStorageOptions();
        Assert.Equal("AuditStorage", AuditStorageOptions.SectionName);
        Assert.Equal(string.Empty, audit.ConnectionString);
        Assert.Equal("NotificationAudit", audit.TableName);

        var acs = new AzureCommunicationServicesOptions();
        Assert.Equal("AzureCommunicationServices", AzureCommunicationServicesOptions.SectionName);
        Assert.Equal(string.Empty, acs.ConnectionString);
        Assert.Equal(string.Empty, acs.SenderAddress);
        Assert.Equal(string.Empty, acs.CallerPhoneNumber);
        Assert.Equal(string.Empty, acs.CallbackBaseUrl);

        var blob = new BlobStorageOptions { ConnectionString = "cs" };
        Assert.Equal("BlobStorage", BlobStorageOptions.SectionName);
        Assert.Equal("cs", blob.ConnectionString);
        Assert.Equal("notifications", blob.ContainerName);

        var email = new EmailOptions();
        Assert.Equal("Email", EmailOptions.SectionName);
        Assert.Equal("Smtp", email.Provider);
        Assert.Equal(string.Empty, email.From);
        Assert.NotNull(email.Smtp);

        var serviceBus = new ServiceBusOptions();
        Assert.Equal("ServiceBus", ServiceBusOptions.SectionName);
        Assert.Null(serviceBus.ConnectionString);
        Assert.Null(serviceBus.Namespace);
        Assert.False(serviceBus.UseInMemory);
        Assert.Equal("email", serviceBus.EmailQueue);
        Assert.Equal("sms", serviceBus.SmsQueue);
        Assert.Equal("telegram", serviceBus.TelegramQueue);

        var smtp = new SmtpOptions();
        Assert.Equal("localhost", smtp.Host);
        Assert.Equal(2525, smtp.Port);
        Assert.Null(smtp.Username);
        Assert.Null(smtp.Password);
        Assert.False(smtp.UseSsl);

        var voice = new VoiceOptions();
        Assert.Equal("Voice", VoiceOptions.SectionName);
        Assert.Equal("Mock", voice.Provider);
        Assert.Equal(60, voice.AudioUrlValidityMinutes);
        Assert.Equal(0, voice.MaxCallAttempts);
    }
}

public class InfrastructureServiceExtensionsTests
{
    private static IConfiguration Config(
        string? emailProvider = "Smtp",
        string? voiceProvider = "Mock")
    {
        var values = new Dictionary<string, string?>
        {
            ["ServiceBus:ConnectionString"] =
                "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=key;UseDevelopmentEmulator=true;"
        };

        if (emailProvider is not null)
            values["Email:Provider"] = emailProvider;

        if (voiceProvider is not null)
            values["Voice:Provider"] = voiceProvider;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static Type? Implementation<TService>(IServiceCollection services) =>
        services.Single(x => x.ServiceType == typeof(TService)).ImplementationType;

    [Theory]
    [InlineData("Smtp", typeof(Smtp4DevEmailSender))]
    [InlineData("AzureCommunicationServices", typeof(AzureCommunicationEmailSender))]
    public void AddInfrastructureServices_RegistersEmailProvider(string provider, Type expected)
    {
        var services = new ServiceCollection();

        var result = services.AddInfrastructureServices(Config(emailProvider: provider));

        Assert.Same(services, result);
        Assert.Equal(expected, Implementation<IEmailSender>(services));
    }

    [Theory]
    [InlineData("Mock", typeof(MockVoiceCallSender))]
    [InlineData("AzureCommunicationServices", typeof(AzureCommunicationVoiceCallSender))]
    public void AddInfrastructureServices_RegistersVoiceProvider(string provider, Type expected)
    {
        var services = new ServiceCollection();

        services.AddInfrastructureServices(Config(voiceProvider: provider));

        Assert.Equal(expected, Implementation<IVoiceCallSender>(services));
        Assert.Equal(typeof(ServiceBusNotificationPublisher), Implementation<INotificationPublisher>(services));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Other")]
    public void AddInfrastructureServices_Throws_WhenEmailProviderIsMissingOrUnsupported(string? provider)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddInfrastructureServices(Config(emailProvider: provider)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Other")]
    public void AddInfrastructureServices_Throws_WhenVoiceProviderIsMissingOrUnsupported(string? provider)
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddInfrastructureServices(Config(voiceProvider: provider)));
    }
}

public class MassTransitConfigurationTests
{
    [Fact]
    public void AddNotificationMessaging_Throws_WhenSectionIsMissing()
    {
        var configuration = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddNotificationMessaging(configuration));

        Assert.Contains("ServiceBus configuration", ex.Message);
    }

    [Fact]
    public void AddNotificationMessaging_Throws_WhenConnectionStringIsMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceBus:UseInMemory"] = "true" })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddNotificationMessaging(configuration));

        Assert.Contains("ConnectionString", ex.Message);
    }
}

public class PlaceholderSenderTests
{
    [Fact]
    public async Task SmsSender_IsNotImplemented()
    {
        await Assert.ThrowsAsync<NotImplementedException>(() =>
            new SmsSender().SendAsync("+18173235812", "text", CancellationToken.None));
    }

    [Fact]
    public async Task TelegramSender_IsNotImplemented()
    {
        await Assert.ThrowsAsync<NotImplementedException>(() =>
            new TelegramSender().SendAsync("+18173235812", "text", null, null, CancellationToken.None));
    }
}
