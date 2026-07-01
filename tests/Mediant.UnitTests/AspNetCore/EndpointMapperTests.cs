using System.Reflection;
using Mediant.AspNetCore.Mapping;

namespace Mediant.UnitTests.AspNetCore;

public class EndpointMapperConvertValueTests
{
    // Use reflection to test the private ConvertValue method
    private static readonly MethodInfo ConvertValueMethod =
        typeof(EndpointMapper).GetMethod("ConvertValue", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static object? InvokeConvertValue(string value, Type targetType)
        => ConvertValueMethod.Invoke(null, new object[] { value, targetType });

    public enum TestStatus { Pending, Active, Completed }

    [Theory]
    [InlineData("Active", TestStatus.Active)]
    [InlineData("active", TestStatus.Active)]
    [InlineData("ACTIVE", TestStatus.Active)]
    [InlineData("Pending", TestStatus.Pending)]
    [InlineData("completed", TestStatus.Completed)]
    public void ConvertValue_Enum_ShouldBeCaseInsensitive(string input, TestStatus expected)
    {
        var result = InvokeConvertValue(input, typeof(TestStatus));
        result.Should().Be(expected);
    }

    [Fact]
    public void ConvertValue_Enum_InvalidValue_ShouldReturnNull()
    {
        var result = InvokeConvertValue("nonexistent", typeof(TestStatus));
        result.Should().BeNull();
    }

    [Fact]
    public void ConvertValue_Guid_ShouldParse()
    {
        var guid = Guid.NewGuid();
        var result = InvokeConvertValue(guid.ToString(), typeof(Guid));
        result.Should().Be(guid);
    }

    [Fact]
    public void ConvertValue_Guid_InvalidValue_ShouldReturnNull()
    {
        var result = InvokeConvertValue("not-a-guid", typeof(Guid));
        result.Should().BeNull();
    }

    [Fact]
    public void ConvertValue_Int_ShouldConvert()
    {
        var result = InvokeConvertValue("42", typeof(int));
        result.Should().Be(42);
    }

    [Fact]
    public void ConvertValue_NullableEnum_ShouldParse()
    {
        var result = InvokeConvertValue("active", typeof(TestStatus?));
        result.Should().Be(TestStatus.Active);
    }

    [Fact]
    public void ConvertValue_String_ShouldPassThrough()
    {
        var result = InvokeConvertValue("hello", typeof(string));
        result.Should().Be("hello");
    }

    [Fact]
    public void ConvertValue_Bool_ShouldConvert()
    {
        var result = InvokeConvertValue("true", typeof(bool));
        result.Should().Be(true);
    }

    [Fact]
    public void ConvertValue_InvalidInt_ShouldReturnNull()
    {
        var result = InvokeConvertValue("abc", typeof(int));
        result.Should().BeNull();
    }

    [Fact]
    public void ConvertValue_InvalidGuid_ShouldReturnNull()
    {
        var result = InvokeConvertValue("not-a-guid", typeof(Guid));
        result.Should().BeNull();
    }

    [Fact]
    public void ConvertValue_InvalidEnum_ShouldReturnNull()
    {
        var result = InvokeConvertValue("nonexistent", typeof(TestStatus));
        result.Should().BeNull();
    }
}
