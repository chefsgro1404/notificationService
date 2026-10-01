using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Enums;
using NotificationService.Functions.Http;

namespace NotificationService.Functions.Functions;

/// <summary>
/// Notification log endpoint: reads the NotificationAudit table with filters and pagination, newest first.
/// GET /api/notification-logs?channel=Email&amp;status=Failed&amp;recipient=contoso&amp;from=...&amp;to=...&amp;page=1&amp;pageSize=20
/// </summary>
public sealed class NotificationLogFunction
{
    private readonly IAuditStore _auditStore;
    private readonly ILogger<NotificationLogFunction> _logger;

    /// <summary>
    /// Creates the function.
    /// </summary>
    /// <param name="auditStore">Reads audit records.</param>
    /// <param name="logger">Logger for failures.</param>
    public NotificationLogFunction(
        IAuditStore auditStore,
        ILogger<NotificationLogFunction> logger)
    {
        _auditStore = auditStore;
        _logger = logger;
    }

    /// <summary>
    /// Returns one page of notification audit records.
    /// </summary>
    /// <param name="request">The HTTP request; filters are read from the query string.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>200 OK with a page of records, 400 for invalid filters, or 500 on failure.</returns>
    [Function("NotificationLogs")]
    public async Task<IActionResult> Run(
            [HttpTrigger(
                    AuthorizationLevel.Function,
                    "get",
                    Route = "notification-logs")]
            HttpRequest request,
        CancellationToken cancellationToken)
    {
        var query = request.Query;

        var pagingError = QueryParameters.TryGetPaging(query, out var page, out var pageSize);
        var channelError = QueryParameters.TryGetEnum<NotificationChannel>(query, "channel", out var channel);
        var statusError = QueryParameters.TryGetEnum<NotificationStatus>(query, "status", out var status);
        var fromError = QueryParameters.TryGetDate(query, "from", out var from);
        var toError = QueryParameters.TryGetDate(query, "to", out var to);

        var error =
            pagingError ?? channelError ?? statusError ?? fromError ?? toError ??
            (from >= to ? "from must be earlier than to." : null);

        if (error is not null)
        {
            return QueryParameters.Error(StatusCodes.Status400BadRequest, error);
        }

        try
        {
            var result = await _auditStore.QueryAsync(
                new NotificationLogQuery
                {
                    Channel = channel,
                    Status = status,
                    Recipient = query["recipient"].ToString(),
                    FromUtc = from,
                    ToUtc = to,
                    Page = page,
                    PageSize = pageSize
                },
                cancellationToken);

            return new OkObjectResult(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to read the notification log");

            return QueryParameters.Error(
                StatusCodes.Status500InternalServerError,
                "Unable to read the notification log.");
        }
    }
}
