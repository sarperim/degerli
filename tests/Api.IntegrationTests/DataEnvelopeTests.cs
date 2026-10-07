using System.Text.Json;
using Degerli.Api.Infrastructure;

namespace Degerli.Api.IntegrationTests;

/// <summary>
/// The honest-data envelope (`01` §10.7) is the only response-shape helper domain
/// modules may use: every data response carries <c>asOf</c> + <c>stale</c>;
/// historical series add <c>adjusted</c>; figures add <c>restated</c>; missing data
/// returns an explicit <c>state</c> (and optional <c>coverage</c>) — never
/// blank-as-zero.
/// </summary>
public sealed class DataEnvelopeTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Figure_envelope_carries_as_of_stale_and_restated()
    {
        var envelope = DataEnvelope.Figure(10.0m, new DateOnly(2026, 10, 6), restated: true);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(envelope, Json));
        var root = document.RootElement;

        Assert.Equal("2026-10-06", root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
        Assert.True(root.GetProperty("restated").GetBoolean());
        Assert.Equal(10.0m, root.GetProperty("value").GetDecimal());
    }

    [Fact]
    public void Series_envelope_carries_adjusted()
    {
        var envelope = DataEnvelope.Series(new[] { 1.0m, 2.0m }, new DateOnly(2026, 10, 6), adjusted: true, stale: true);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(envelope, Json));
        var root = document.RootElement;

        Assert.Equal("2026-10-06", root.GetProperty("asOf").GetString());
        Assert.True(root.GetProperty("stale").GetBoolean());
        Assert.True(root.GetProperty("adjusted").GetBoolean());
        Assert.Equal(2, root.GetProperty("points").GetArrayLength());
    }

    [Fact]
    public void Missing_data_envelope_carries_state_not_a_zero()
    {
        var envelope = DataEnvelope.Missing(
            new DateOnly(2026, 10, 6),
            DataStates.NoData,
            new DataCoverage("2026-01-01", "Source does not publish this series."));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(envelope, Json));
        var root = document.RootElement;

        Assert.Equal("2026-10-06", root.GetProperty("asOf").GetString());
        Assert.False(root.GetProperty("stale").GetBoolean());
        Assert.Equal("no-data", root.GetProperty("state").GetString());
        Assert.Equal("2026-01-01", root.GetProperty("coverage").GetProperty("availableFrom").GetString());
        Assert.Equal("Source does not publish this series.", root.GetProperty("coverage").GetProperty("boundaryNote").GetString());
        Assert.False(root.TryGetProperty("value", out _));
    }

    [Theory]
    [InlineData("no-data")]
    [InlineData("preparing")]
    [InlineData("unavailable")]
    public void Data_states_are_the_documented_set(string state)
    {
        Assert.Contains(state, DataStates.All);
    }
}
