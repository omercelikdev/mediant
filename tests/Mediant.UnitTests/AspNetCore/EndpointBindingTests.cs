using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Mediant.AspNetCore.Mapping;

namespace Mediant.UnitTests.AspNetCore;

/// <summary>
/// GET query/route binding for both shapes: parameterless-ctor types (classes, init-property
/// records) and positional records (parameterized primary constructor only).
/// </summary>
public class EndpointBindingTests
{
    // BindFromQueryAndRoute is private: (object? Instance, List<string> Errors)
    private static readonly MethodInfo BindMethod =
        typeof(EndpointMapper).GetMethod("BindFromQueryAndRoute", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static (object? Instance, IReadOnlyList<string> Errors) Bind(
        Type requestType,
        (string Key, string Value)[]? query = null,
        (string Key, object Value)[]? route = null)
    {
        var context = new DefaultHttpContext();
        if (query is not null)
        {
            context.Request.QueryString = new QueryString(
                "?" + string.Join("&", query.Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}")));
        }
        if (route is not null)
        {
            var values = new RouteValueDictionary();
            foreach (var (key, value) in route)
            {
                values[key] = value;
            }
            context.Request.RouteValues = values;
        }

        var tuple = BindMethod.Invoke(null, new object[] { context, requestType })!;
        var instance = tuple.GetType().GetField("Item1")!.GetValue(tuple);
        var errors = (IReadOnlyList<string>)((System.Collections.IEnumerable)tuple.GetType().GetField("Item2")!.GetValue(tuple)!)
            .Cast<string>().ToList();
        return (instance, errors);
    }

    // === Positional records ===

    public sealed record GetOrdersQuery(string? Cursor = null, int Size = 50);

    [Fact]
    public void PositionalRecord_Binds_From_QueryString()
    {
        var (instance, errors) = Bind(typeof(GetOrdersQuery),
            query: [("Cursor", "abc"), ("Size", "25")]);

        errors.Should().BeEmpty();
        var q = instance.Should().BeOfType<GetOrdersQuery>().Subject;
        q.Cursor.Should().Be("abc");
        q.Size.Should().Be(25);
    }

    [Fact]
    public void PositionalRecord_Missing_Optionals_Fall_Back_To_Declared_Defaults()
    {
        var (instance, errors) = Bind(typeof(GetOrdersQuery));

        errors.Should().BeEmpty();
        var q = instance.Should().BeOfType<GetOrdersQuery>().Subject;
        q.Cursor.Should().BeNull();
        q.Size.Should().Be(50, "the constructor's declared default must be used when the value is absent");
    }

    [Fact]
    public void PositionalRecord_Is_Case_Insensitive_On_Parameter_Names()
    {
        var (instance, errors) = Bind(typeof(GetOrdersQuery),
            query: [("cursor", "x"), ("SIZE", "7")]);

        errors.Should().BeEmpty();
        var q = instance.Should().BeOfType<GetOrdersQuery>().Subject;
        q.Cursor.Should().Be("x");
        q.Size.Should().Be(7);
    }

    public sealed record GetByIdQuery(int Id);

    [Fact]
    public void PositionalRecord_Required_Value_Type_Missing_Reports_Error_Not_Throws()
    {
        var (instance, errors) = Bind(typeof(GetByIdQuery));

        instance.Should().BeNull();
        errors.Should().ContainSingle().Which.Should().Contain("Id").And.Contain("missing");
    }

    [Fact]
    public void PositionalRecord_Invalid_Value_Reports_Error()
    {
        var (instance, errors) = Bind(typeof(GetByIdQuery), query: [("Id", "not-an-int")]);

        instance.Should().BeNull();
        errors.Should().ContainSingle().Which.Should().Contain("Id").And.Contain("invalid");
    }

    [Fact]
    public void PositionalRecord_Binds_Route_Value()
    {
        var (instance, errors) = Bind(typeof(GetByIdQuery), route: [("Id", "42")]);

        errors.Should().BeEmpty();
        instance.Should().BeOfType<GetByIdQuery>().Subject.Id.Should().Be(42);
    }

    public sealed record HybridQuery(int Page)
    {
        public string? Filter { get; init; }
    }

    [Fact]
    public void PositionalRecord_Also_Binds_Extra_Init_Properties()
    {
        var (instance, errors) = Bind(typeof(HybridQuery),
            query: [("Page", "3"), ("Filter", "active")]);

        errors.Should().BeEmpty();
        var q = instance.Should().BeOfType<HybridQuery>().Subject;
        q.Page.Should().Be(3);
        q.Filter.Should().Be("active");
    }

    // === Regression: parameterless-ctor shapes still bind via properties ===

    public sealed class InitQuery
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }

    [Fact]
    public void ParameterlessCtor_Class_Still_Binds_Via_Properties()
    {
        var (instance, errors) = Bind(typeof(InitQuery), query: [("Name", "bob"), ("Count", "9")]);

        errors.Should().BeEmpty();
        var q = instance.Should().BeOfType<InitQuery>().Subject;
        q.Name.Should().Be("bob");
        q.Count.Should().Be(9);
    }

    public sealed record InitRecord
    {
        public Guid Id { get; init; }
    }

    [Fact]
    public void InitProperty_Record_Still_Binds()
    {
        var id = Guid.NewGuid();
        var (instance, errors) = Bind(typeof(InitRecord), route: [("Id", id.ToString())]);

        errors.Should().BeEmpty();
        instance.Should().BeOfType<InitRecord>().Subject.Id.Should().Be(id);
    }
}
