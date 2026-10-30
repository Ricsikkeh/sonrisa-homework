using WorldEventAlerts.Api.Features.Accounts.Domain;

namespace WorldEventAlerts.Api.Tests;

public sealed class UserNameTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Missing_or_blank_values_cannot_be_constructed(string? value)
    {
        Assert.False(UserName.TryCreate(value, out var userName));
        Assert.Null(userName);
    }

    [Fact]
    public void Length_limit_is_enforced_at_the_value_boundary()
    {
        Assert.True(UserName.TryCreate(new string('a', 256), out var maximum));
        Assert.Equal(256, maximum!.Value.Length);
        Assert.False(UserName.TryCreate(new string('a', 257), out var oversized));
        Assert.Null(oversized);
    }

    [Fact]
    public void Values_are_immutable_records_with_value_equality_and_no_public_construction_bypass()
    {
        Assert.True(UserName.TryCreate("Alice", out var first));
        Assert.True(UserName.TryCreate("Alice", out var second));
        Assert.Equal(first, second);
        Assert.Equal(first, first! with { });
        Assert.Empty(typeof(UserName).GetConstructors());
        Assert.Null(typeof(UserName).GetProperty(nameof(UserName.Value))!.SetMethod);
        Assert.Equal("Alice", first.Value);
    }

    [Fact]
    public void Character_policy_normalization_and_uniqueness_are_not_reimplemented()
    {
        // Structural validity is shared by registration/login. Identity alone owns
        // registration's configurable character policy and case-normalized uniqueness.
        Assert.True(UserName.TryCreate(" Alice ", out var value));
        Assert.Equal(" Alice ", value!.Value);
        Assert.True(UserName.TryCreate("Alice", out var upper));
        Assert.True(UserName.TryCreate("alice", out var lower));
        Assert.NotEqual(upper, lower);
    }
}
