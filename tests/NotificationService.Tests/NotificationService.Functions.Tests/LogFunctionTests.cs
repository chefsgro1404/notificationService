using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using NotificationService.Application.Exceptions;
using NotificationService.Application.Interfaces;
using NotificationService.Application.Models;
using NotificationService.Domain.Enums;
using NotificationService.Functions.Functions;
using NotificationService.Functions.Http;

namespace NotificationService.Functions.Tests.Functions;

internal static class LogRequests
{
    public static HttpRequest Get(params (string Key, string Value)[] query)
    {
        var context = new DefaultHttpContext();
        context.Request.Query = new QueryCollection(
            query.ToDictionary(x => x.Key, x => new StringValues(x.Value)));
        return context.Request;
    }

    public static HttpRequest Json(string body, string contentType = "application/json")
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context.Request;
    }

    public static (int? Status, string? Error) ErrorOf(IActionResult result)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        var error = objectResult.Value!.GetType().GetProperty("error")?.GetValue(objectResult.Value) as string;
        return (objectResult.StatusCode, error);
    }

    public static PagedResult<T> Empty<T>() =>
        new() { Items = [], Page = 1, PageSize = 20, TotalCount = 0 };
}

public class QueryParametersTests
{
    private static IQueryCollection Query(params (string Key, string Value)[] values) =>
        LogRequests.Get(values).Query;

    [Fact]
    public void TryGetPaging_UsesDefaults()
    {
        Assert.Null(QueryParameters.TryGetPaging(Query(), out var page, out var size));
        Assert.Equal((1, 20), (page, size));
    }

    [Fact]
    public void TryGetPaging_ReadsValues()
    {
        Assert.Null(QueryParameters.TryGetPaging(Query(("page", "3"), ("pageSize", "100")), out var page, out var size));
        Assert.Equal((3, 100), (page, size));
    }

    [Theory]
    [InlineData("page", "0")]
    [InlineData("page", "-1")]
    [InlineData("page", "x")]
    [InlineData("pageSize", "0")]
    [InlineData("pageSize", "101")]
    [InlineData("pageSize", "1.5")]
    public void TryGetPaging_RejectsInvalidValues(string key, string value)
    {
        Assert.NotNull(QueryParameters.TryGetPaging(Query((key, value)), out _, out _));
    }

    [Fact]
    public void TryGetEnum_ParsesNamesCaseInsensitively_AndRejectsNumbers()
    {
        Assert.Null(QueryParameters.TryGetEnum<NotificationStatus>(Query(("s", "failed")), "s", out var parsed));
        Assert.Equal(NotificationStatus.Failed, parsed);

        Assert.Null(QueryParameters.TryGetEnum<NotificationStatus>(Query(("s", " ")), "s", out var empty));
        Assert.Null(empty);

        Assert.Equal(
            "s must be one of: Accepted, Queued, Processing, Sent, Retrying, Failed.",
            QueryParameters.TryGetEnum<NotificationStatus>(Query(("s", "4")), "s", out var numeric));
        Assert.Null(numeric);
    }

    [Fact]
    public void TryGetDate_ParsesIsoAndAssumesUtc()
    {
        Assert.Null(QueryParameters.TryGetDate(Query(("d", "2026-09-30T10:00:00+06:00")), "d", out var withOffset));
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 4, 0, 0, TimeSpan.Zero), withOffset);

        Assert.Null(QueryParameters.TryGetDate(Query(("d", "2026-09-30")), "d", out var dateOnly));
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), dateOnly);

        Assert.Null(QueryParameters.TryGetDate(Query(), "d", out var missing));
        Assert.Null(missing);

        Assert.NotNull(QueryParameters.TryGetDate(Query(("d", "yesterday")), "d", out _));
    }

    [Theory]
    [InlineData("Deleted", true)]
    [InlineData(" inactive ", true)]
    [InlineData("3", false)]
    [InlineData("Active,Inactive", false)]
    [InlineData(null, false)]
    public void TryParseName_AcceptsOnlyMemberNames(string? raw, bool expected)
    {
        Assert.Equal(expected, QueryParameters.TryParseName<AudioStatus>(raw, out _));
    }
}

public class NotificationLogFunctionTests
{
    private readonly Mock<IAuditStore> _auditStore = new();

    private NotificationLogFunction CreateFunction() =>
        new(_auditStore.Object, Mock.Of<ILogger<NotificationLogFunction>>());

    [Fact]
    public async Task Run_PassesParsedFiltersToTheStore()
    {
        NotificationLogQuery? captured = null;
        var expected = LogRequests.Empty<NotificationLogItem>();

        _auditStore
            .Setup(x => x.QueryAsync(It.IsAny<NotificationLogQuery>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationLogQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(expected);

        var result = await CreateFunction().Run(
            LogRequests.Get(
                ("channel", "email"),
                ("status", "Failed"),
                ("recipient", "contoso"),
                ("from", "2026-09-01T00:00:00Z"),
                ("to", "2026-09-02T00:00:00Z"),
                ("page", "2"),
                ("pageSize", "50")),
            CancellationToken.None);

        Assert.Same(expected, Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(NotificationChannel.Email, captured!.Channel);
        Assert.Equal(NotificationStatus.Failed, captured.Status);
        Assert.Equal("contoso", captured.Recipient);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), captured.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero), captured.ToUtc);
        Assert.Equal((2, 50), (captured.Page, captured.PageSize));
    }

    [Theory]
    [InlineData("page", "0")]
    [InlineData("channel", "Fax")]
    [InlineData("status", "Lost")]
    [InlineData("from", "bad")]
    [InlineData("to", "bad")]
    public async Task Run_ReturnsBadRequest_ForInvalidFilters(string key, string value)
    {
        var result = await CreateFunction().Run(LogRequests.Get((key, value)), CancellationToken.None);

        Assert.Equal(400, LogRequests.ErrorOf(result).Status);
        _auditStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Run_ReturnsBadRequest_WhenFromIsNotBeforeTo()
    {
        var result = await CreateFunction().Run(
            LogRequests.Get(("from", "2026-09-02"), ("to", "2026-09-02")),
            CancellationToken.None);

        Assert.Equal((400, "from must be earlier than to."), LogRequests.ErrorOf(result));
    }

    [Fact]
    public async Task Run_ReturnsServerError_WhenStoreFails()
    {
        _auditStore
            .Setup(x => x.QueryAsync(It.IsAny<NotificationLogQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("down"));

        var result = await CreateFunction().Run(LogRequests.Get(), CancellationToken.None);

        Assert.Equal((500, "Unable to read the notification log."), LogRequests.ErrorOf(result));
    }

    [Fact]
    public async Task Run_PropagatesCancellation()
    {
        _auditStore
            .Setup(x => x.QueryAsync(It.IsAny<NotificationLogQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateFunction().Run(LogRequests.Get(), CancellationToken.None));
    }
}

public class TextToSpeechLogFunctionTests
{
    private readonly Mock<ITextToSpeechService> _service = new();

    private TextToSpeechLogFunction CreateFunction() =>
        new(_service.Object, Mock.Of<ILogger<TextToSpeechLogFunction>>());

    private static TextToSpeechLogItem Item(int id, AudioStatus status) => new()
    {
        Id = id,
        NotificationId = Guid.NewGuid(),
        Text = "Hello",
        BlobName = $"tts/{id}.wav",
        ContentType = "audio/wav",
        SizeBytes = 1,
        CharacterCount = 5,
        Status = status,
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        UpdatedAtUtc = DateTimeOffset.UnixEpoch
    };

    private TextToSpeechLogQuery? CaptureQuery()
    {
        TextToSpeechLogQuery? captured = null;
        _service
            .Setup(x => x.GetLogsAsync(It.IsAny<TextToSpeechLogQuery>(), It.IsAny<CancellationToken>()))
            .Callback<TextToSpeechLogQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(LogRequests.Empty<TextToSpeechLogItem>());
        return captured;
    }

    [Theory]
    [InlineData(null, new[] { AudioStatus.AudioNotGenerated, AudioStatus.Active, AudioStatus.Inactive })]
    [InlineData("", new[] { AudioStatus.AudioNotGenerated, AudioStatus.Active, AudioStatus.Inactive })]
    [InlineData("all", new[] { AudioStatus.AudioNotGenerated, AudioStatus.Active, AudioStatus.Inactive, AudioStatus.Deleted })]
    [InlineData("AudioNotGenerated", new[] { AudioStatus.AudioNotGenerated })]
    [InlineData("deleted", new[] { AudioStatus.Deleted })]
    public async Task List_MapsStatusFilter(string? status, AudioStatus[] expected)
    {
        TextToSpeechLogQuery? captured = null;
        _service
            .Setup(x => x.GetLogsAsync(It.IsAny<TextToSpeechLogQuery>(), It.IsAny<CancellationToken>()))
            .Callback<TextToSpeechLogQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(LogRequests.Empty<TextToSpeechLogItem>());

        var query = status is null
            ? LogRequests.Get(("search", "hi"), ("page", "2"), ("pageSize", "5"))
            : LogRequests.Get(("status", status), ("search", "hi"), ("page", "2"), ("pageSize", "5"));

        var result = await CreateFunction().List(query, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(expected, captured!.Statuses);
        Assert.Equal(("hi", 2, 5), (captured.Search, captured.Page, captured.PageSize));
    }

    [Theory]
    [InlineData("status", "Archived")]
    [InlineData("pageSize", "500")]
    public async Task List_ReturnsBadRequest_ForInvalidFilters(string key, string value)
    {
        CaptureQuery();

        var result = await CreateFunction().List(LogRequests.Get((key, value)), CancellationToken.None);

        Assert.Equal(400, LogRequests.ErrorOf(result).Status);
    }

    [Fact]
    public async Task List_ReturnsServerError_WhenServiceFails()
    {
        _service
            .Setup(x => x.GetLogsAsync(It.IsAny<TextToSpeechLogQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("down"));

        var result = await CreateFunction().List(LogRequests.Get(), CancellationToken.None);

        Assert.Equal((500, "Unable to list audio."), LogRequests.ErrorOf(result));
    }

    [Fact]
    public async Task Get_ReturnsItem_Or404()
    {
        var item = Item(9, AudioStatus.Active);
        _service.Setup(x => x.GetLogAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        var found = await CreateFunction().Get(LogRequests.Get(), 9, CancellationToken.None);
        var missing = await CreateFunction().Get(LogRequests.Get(), 10, CancellationToken.None);

        Assert.Same(item, Assert.IsType<OkObjectResult>(found).Value);
        Assert.Equal((404, "Audio 10 was not found."), LogRequests.ErrorOf(missing));
    }

    [Fact]
    public async Task UpdateStatus_ChangesStatus()
    {
        var item = Item(4, AudioStatus.Inactive);
        _service.Setup(x => x.UpdateStatusAsync(4, AudioStatus.Inactive, It.IsAny<CancellationToken>())).ReturnsAsync(item);

        var result = await CreateFunction().UpdateStatus(LogRequests.Json("{\"status\":\"inactive\"}"), 4, CancellationToken.None);

        Assert.Same(item, Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task UpdateStatus_Returns404_WhenAudioIsMissing()
    {
        var result = await CreateFunction().UpdateStatus(LogRequests.Json("{\"status\":\"Deleted\"}"), 99, CancellationToken.None);

        Assert.Equal(404, LogRequests.ErrorOf(result).Status);
    }

    [Theory]
    [InlineData("{\"status\":\"Archived\"}", "application/json")]
    [InlineData("{\"status\":2}", "application/json")]
    [InlineData("{}", "application/json")]
    [InlineData("null", "application/json")]
    [InlineData("{bad", "application/json")]
    [InlineData("status=Active", "text/plain")]
    public async Task UpdateStatus_ReturnsBadRequest_ForInvalidBody(string body, string contentType)
    {
        var result = await CreateFunction().UpdateStatus(LogRequests.Json(body, contentType), 1, CancellationToken.None);

        Assert.Equal(400, LogRequests.ErrorOf(result).Status);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateStatus_ReturnsConflict_WhenChangeIsNotAllowed()
    {
        _service
            .Setup(x => x.UpdateStatusAsync(3, AudioStatus.Active, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AudioStatusChangeException("Audio 3 was never generated, so it can only be deleted."));

        var result = await CreateFunction().UpdateStatus(LogRequests.Json("{\"status\":\"Active\"}"), 3, CancellationToken.None);

        Assert.Equal((409, "Audio 3 was never generated, so it can only be deleted."), LogRequests.ErrorOf(result));
    }

    [Fact]
    public async Task UpdateStatus_ReturnsServerError_WhenServiceFails()
    {
        _service
            .Setup(x => x.UpdateStatusAsync(It.IsAny<int>(), It.IsAny<AudioStatus>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("down"));

        var result = await CreateFunction().UpdateStatus(LogRequests.Json("{\"status\":\"Active\"}"), 1, CancellationToken.None);

        Assert.Equal((500, "Unable to update audio status."), LogRequests.ErrorOf(result));
    }

    [Fact]
    public async Task Get_PropagatesCancellation()
    {
        _service
            .Setup(x => x.GetLogAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateFunction().Get(LogRequests.Get(), 1, CancellationToken.None));
    }
}
