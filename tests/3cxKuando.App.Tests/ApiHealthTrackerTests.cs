using System.Net;
using KuandoConnector.App;
using Xunit;

namespace KuandoConnector.App.Tests;

public sealed class ApiHealthTrackerTests
{
    [Fact]
    public void LogsEachNewFailureOnceAndLogsRecovery()
    {
        var messages = new List<(string Category, string Message)>();
        var tracker = new ApiHealthTracker((category, message) => messages.Add((category, message)));
        tracker.Failed("Presence", new HttpRequestException("secret URL must not appear", null, HttpStatusCode.Unauthorized));
        tracker.Failed("Presence", new HttpRequestException("secret URL must not appear", null, HttpStatusCode.Unauthorized));
        tracker.Failed("Call Control", new HttpRequestException("secret URL must not appear", null, HttpStatusCode.Forbidden));
        Assert.Equal(new[] { "Presence: HTTP 401", "Call Control: HTTP 403" }, tracker.ActiveErrors);
        Assert.Equal(new[]
        {
            ("3CX Presence error", "HTTP 401"),
            ("3CX Call Control error", "HTTP 403")
        }, messages);
        Assert.DoesNotContain(messages, x => x.Message.Contains("secret"));
        tracker.Succeeded("Presence");
        tracker.Succeeded("Presence");
        Assert.Equal(new[] { "Call Control: HTTP 403" }, tracker.ActiveErrors);
        Assert.Equal(("3CX Presence", "Recovered"), messages[^1]);
        tracker.Succeeded("Call Control");
        Assert.Empty(tracker.ActiveErrors);
        Assert.Equal(("3CX Call Control", "Recovered"), messages[^1]);
    }

    [Fact]
    public void ChangedErrorCodeIsLoggedAndRetainedUntilSuccessfulRequest()
    {
        var messages = new List<string>();
        var tracker = new ApiHealthTracker((category, message) => messages.Add(category + ": " + message));
        tracker.Failed("Presence", new HttpRequestException("401", null, HttpStatusCode.Unauthorized));
        tracker.Failed("Presence", new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable));
        Assert.Equal(new[] { "Presence: HTTP 503" }, tracker.ActiveErrors);
        Assert.Equal(2, messages.Count);
    }
}
