using McpHub.Modules.Sql;

namespace McpHub.UnitTests.Database;

public class SqlValueFormatterTests
{
    [Fact]
    public void Nulls_and_dbnull_become_null()
    {
        Assert.Null(ValueFormatter.Format(null, 100));
        Assert.Null(ValueFormatter.Format(DBNull.Value, 100));
    }

    [Fact]
    public void Long_text_is_truncated()
    {
        var result = (string?)ValueFormatter.Format(new string('x', 500), 10);

        Assert.StartsWith("xxxxxxxxxx", result, StringComparison.Ordinal);
        Assert.EndsWith("[truncated]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Binary_values_are_summarised_not_returned()
    {
        Assert.Equal("[binary 3 bytes]", ValueFormatter.Format(new byte[] { 1, 2, 3 }, 100));
    }

    [Fact]
    public void Non_finite_numbers_become_strings_so_json_serialisation_cannot_fail()
    {
        Assert.IsType<string>(ValueFormatter.Format(double.NaN, 100));
        Assert.IsType<string>(ValueFormatter.Format(float.PositiveInfinity, 100));
    }

    [Fact]
    public void Dates_use_round_trip_format()
    {
        var value = new DateTime(2026, 11, 6, 10, 30, 0, DateTimeKind.Utc);

        Assert.Equal("2026-11-06T10:30:00.0000000Z", ValueFormatter.Format(value, 100));
    }

    [Fact]
    public void Arrays_are_formatted_element_by_element()
    {
        var result = Assert.IsAssignableFrom<IReadOnlyList<object?>>(ValueFormatter.Format(new[] { 1, 2 }, 100));

        Assert.Equal(2, result.Count);
    }
}
