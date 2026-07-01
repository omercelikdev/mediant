using Mediant.Guards;

namespace Mediant.UnitTests.Guards;

public class GuardTests
{
    [Fact]
    public void Against_WithNull_ShouldThrow()
    {
        string? value = null;
        var act = () => Guard.Against(value);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Against_WithValue_ShouldReturnValue()
    {
        var result = Guard.Against("hello");
        result.Should().Be("hello");
    }

    [Fact]
    public void AgainstNullOrEmpty_WithNull_ShouldThrow()
    {
        var act = () => Guard.AgainstNullOrEmpty((string?)null);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AgainstNullOrEmpty_WithEmpty_ShouldThrow()
    {
        var act = () => Guard.AgainstNullOrEmpty(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AgainstNullOrEmpty_WithValue_ShouldReturnValue()
    {
        Guard.AgainstNullOrEmpty("hello").Should().Be("hello");
    }

    [Fact]
    public void AgainstNullOrWhiteSpace_WithWhitespace_ShouldThrow()
    {
        var act = () => Guard.AgainstNullOrWhiteSpace("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AgainstNegative_WithNegative_ShouldThrow()
    {
        var act = () => Guard.AgainstNegative(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AgainstNegative_WithZero_ShouldReturnValue()
    {
        Guard.AgainstNegative(0).Should().Be(0);
    }

    [Fact]
    public void AgainstZeroOrNegative_WithZero_ShouldThrow()
    {
        var act = () => Guard.AgainstZeroOrNegative(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AgainstZeroOrNegative_WithPositive_ShouldReturnValue()
    {
        Guard.AgainstZeroOrNegative(5).Should().Be(5);
    }

    [Fact]
    public void AgainstNullOrEmpty_Collection_WithEmpty_ShouldThrow()
    {
        var act = () => Guard.AgainstNullOrEmpty(Array.Empty<int>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AgainstNullOrEmpty_Collection_WithItems_ShouldReturnCollection()
    {
        var list = new[] { 1, 2, 3 };
        Guard.AgainstNullOrEmpty<int>(list).Should().HaveCount(3);
    }
}
