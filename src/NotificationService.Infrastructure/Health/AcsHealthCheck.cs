using Azure.Communication.PhoneNumbers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NotificationService.Infrastructure.Configuration;

namespace NotificationService.Infrastructure.Health;

/// <summary>
/// Checks Azure Communication Services without sending anything: no email is sent and no call is placed.
/// Voice: reads the caller phone number from the resource (a free management read) to prove the access key
/// works and the number still belongs to the resource with outbound calling enabled.
/// Email only: lists one page of purchased numbers to prove the access key works, and checks the sender address.
/// When neither email nor voice uses ACS (Smtp / Mock providers), the block reports "not in use".
/// </summary>
public sealed class AcsHealthCheck : IHealthCheck
{
    private const string AcsProvider = "AzureCommunicationServices";

    private readonly Lazy<PhoneNumbersClient> _client;
    private readonly AzureCommunicationServicesOptions _acs;
    private readonly bool _emailUsesAcs;
    private readonly bool _voiceUsesAcs;

    /// <summary>
    /// Creates the check from the "AzureCommunicationServices", "Email" and "Voice" settings.
    /// </summary>
    /// <param name="acsOptions">ACS connection, sender and caller settings.</param>
    /// <param name="emailOptions">Email settings; the provider decides whether email uses ACS.</param>
    /// <param name="voiceOptions">Voice settings; the provider decides whether calls use ACS.</param>
    public AcsHealthCheck(
        IOptions<AzureCommunicationServicesOptions> acsOptions,
        IOptions<EmailOptions> emailOptions,
        IOptions<VoiceOptions> voiceOptions)
        : this(
            () => new PhoneNumbersClient(acsOptions.Value.ConnectionString),
            acsOptions.Value,
            IsAcs(emailOptions.Value.Provider),
            IsAcs(voiceOptions.Value.Provider))
    {
    }

    internal AcsHealthCheck(
        Func<PhoneNumbersClient> createClient,
        AzureCommunicationServicesOptions acs,
        bool emailUsesAcs,
        bool voiceUsesAcs)
    {
        _client = new Lazy<PhoneNumbersClient>(createClient);
        _acs = acs;
        _emailUsesAcs = emailUsesAcs;
        _voiceUsesAcs = voiceUsesAcs;
    }

    private static bool IsAcs(string? provider) =>
        string.Equals(provider, AcsProvider, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Validates the ACS configuration and access key with read-only management calls.
    /// </summary>
    /// <param name="context">The health check context.</param>
    /// <param name="cancellationToken">A token used to cancel the check (also fired on timeout).</param>
    /// <returns>Healthy, or Unhealthy with the reason; Healthy "not in use" when ACS is not configured as a provider.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["email"] = _emailUsesAcs ? "AzureCommunicationServices" : "other provider",
            ["voice"] = _voiceUsesAcs ? "AzureCommunicationServices" : "other provider"
        };

        if (!_emailUsesAcs && !_voiceUsesAcs)
        {
            return HealthCheckResult.Healthy("Not in use: email and voice use other providers.", data);
        }

        var configError = ConfigurationError();

        if (configError is not null)
        {
            return HealthCheckResult.Unhealthy(configError, data: data);
        }

        try
        {
            if (_voiceUsesAcs)
            {
                PurchasedPhoneNumber number = await _client.Value.GetPurchasedPhoneNumberAsync(
                    _acs.CallerPhoneNumber.Trim(),
                    cancellationToken);

                var calling = number.Capabilities.Calling;
                data["callerNumberCalling"] = calling.ToString();

                if (calling != PhoneNumberCapabilityType.Outbound
                    && calling != PhoneNumberCapabilityType.InboundOutbound)
                {
                    return HealthCheckResult.Unhealthy(
                        "The caller phone number cannot place outbound calls.",
                        data: data);
                }
            }
            else
            {
                await foreach (var _ in _client.Value
                                   .GetPurchasedPhoneNumbersAsync(cancellationToken)
                                   .AsPages(pageSizeHint: 1))
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                $"ACS check failed: {HealthFailure.Reason(ex)}.",
                data: data);
        }

        return HealthCheckResult.Healthy("ACS access key and configuration are valid.", data);
    }

    private string? ConfigurationError()
    {
        if (string.IsNullOrWhiteSpace(_acs.ConnectionString))
        {
            return "AzureCommunicationServices:ConnectionString is not configured.";
        }

        if (_emailUsesAcs && string.IsNullOrWhiteSpace(_acs.SenderAddress))
        {
            return "AzureCommunicationServices:SenderAddress is not configured.";
        }

        if (_voiceUsesAcs && string.IsNullOrWhiteSpace(_acs.CallerPhoneNumber))
        {
            return "AzureCommunicationServices:CallerPhoneNumber is not configured.";
        }

        if (_voiceUsesAcs
            && (!Uri.TryCreate(_acs.CallbackBaseUrl, UriKind.Absolute, out var callback)
                || callback.Scheme != Uri.UriSchemeHttps))
        {
            return "AzureCommunicationServices:CallbackBaseUrl must be an https:// URL.";
        }

        return null;
    }
}
