using FluentAssertions;
using Space.Map.Core.Formatting;
using Xunit;

namespace Space.Map.Core.Tests;

public class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(1099511627776, "1.0 TB")]
    public void Format_PositiveBytes_ReturnsExpectedString(long bytes, string expected)
    {
        var result = ByteSizeFormatter.Format(bytes);
        result.Should().Be(expected);
    }

    [Fact]
    public void Format_NegativeBytes_ReturnsNegativeFormattedString()
    {
        var result = ByteSizeFormatter.Format(-1024);
        result.Should().Be("-1.0 KB");
    }

    [Fact]
    public void Format_LongMinValue_DoesNotOverflow()
    {
        var action = () => ByteSizeFormatter.Format(long.MinValue);
        action.Should().NotThrow();
        action().Should().StartWith("-");
    }
}
