using System.Text;
using Takupoke.Infrastructure.Api;
using Xunit;

namespace Takupoke.Integration.Tests;

public sealed class PublicStatusTests
{
    [Fact]
    public void FunctionalStatusKeepsPdfEvidenceAndReadinessSeparate()
    {
        var status = """
          {"schemaVersion":1,"service":"takupoke-api","state":"ok",
           "checkedAt":"2032-04-01T00:00:00.123Z","validUntil":"2032-04-01T00:03:00.123Z",
           "features":[{"id":"events","required":true,"state":"ok","checkedAt":"2032-04-01T00:00:00.123Z",
            "validUntil":"2032-04-01T00:03:00.123Z","lastSuccessfulAt":"2032-04-01T00:00:00.123Z"}],
           "data":{"events":{"checkedAt":"2032-04-01T00:00:00.123Z","validUntil":"2032-04-01T00:03:00.123Z","versions":[
            {"schoolYear":2032,"format":"v1","dataVersion":"sha256-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
             "sourceState":"source_differs","sourceCheckedAt":"2032-03-31T23:59:00Z","reviewState":"unknown","reviewCheckedAt":null}]}}}
          """;
        var bytes = Encoding.UTF8.GetBytes(status);
        var value = FunctionalStatus.Decode(bytes, PublicService.Api);
        Assert.Throws<ApiException>(() => FunctionalStatus.Decode(bytes, PublicService.Authentication));
        Assert.Equal(FunctionalState.Available, value.Find("events")!.Current(value.CheckedAt));
        Assert.Equal(FunctionalState.Unknown, value.Find("events")!.Current(value.ValidUntil));
        Assert.Equal("source_differs", value.Data!.Events!.Find(2032, "v1", value.CheckedAt)?.SourceState);
        Assert.Null(value.Data.Events.Find(2032, "v2", value.CheckedAt));
        Assert.Null(value.Data.Events.Find(2032, "v1", value.ValidUntil));
    }

    [Fact]
    public void PublicHistoryIgnoresUnrelatedOutageAndDuplicateDates()
    {
        var payload = """
          {"data":{"id":"267223","type":"status_page","attributes":{"aggregate_state":"downtime"},
            "relationships":{"resources":{"data":[{"id":"9028864","type":"status_page_resource"}]},
             "status_reports":{"data":[{"id":"fictional","type":"status_report"}]}}},
           "included":[
            {"id":"9028864","type":"status_page_resource","attributes":{"resource_type":"Monitor","status":"operational",
             "availability":0.999,"status_history":[{"day":"2032-04-01","status":"operational"},
                {"day":"2032-04-01","status":"downtime"},{"day":"2032-04-02","status":"not_monitored"}]}},
            {"id":"9028865","type":"status_page_resource","attributes":{"resource_type":"Monitor","status":"downtime"}},
            {"id":"fictional","type":"status_report","attributes":{"title":"架空サービスの障害",
             "affected_resources":[{"status_page_resource_id":"9028865","status":"downtime"}]}}]}
          """;
        var status = PublicMonitoring.Decode(Encoding.UTF8.GetBytes(payload));
        var monitor = Assert.Single(status.Services).Value;
        Assert.Equal("operational", monitor.State);
        Assert.Equal(0.999, monitor.Availability);
        Assert.Equal(["unknown", "not_monitored"], monitor.History.Select(day => day.State).ToArray());
        Assert.Empty(monitor.Incidents);
    }
}
