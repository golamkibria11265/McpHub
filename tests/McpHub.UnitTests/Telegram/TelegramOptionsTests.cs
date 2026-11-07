using McpHub.Modules.Telegram;

namespace McpHub.UnitTests.Telegram;

public sealed class TelegramOptionsTests
{
    [Theory]
    [InlineData(null, 50)]
    [InlineData(0, 1)]
    [InlineData(10, 10)]
    [InlineData(500, 50)]
    public void Counts_are_clamped_to_the_configured_maximum(int? requested, int expected)
    {
        Assert.Equal(expected, new TelegramOptions().ClampCount(requested));
    }
}
