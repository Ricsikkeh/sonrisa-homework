using WorldEventAlerts.Api.Features.Accounts.Contracts;
using WorldEventAlerts.Api.Features.Accounts.Controllers;

namespace WorldEventAlerts.Api.Tests;

public sealed class CredentialsRequestValidatorTests
{
    [Theory]
    [InlineData(null, null, true, true)]
    [InlineData("", "", true, true)]
    [InlineData(" \t", "secret", true, false)]
    [InlineData("alice", null, false, true)]
    [InlineData("alice", "", false, true)]
    public void Request_validation_returns_safe_field_codes_without_creating_application_input(
        string? userName, string? password, bool invalidName, bool invalidPassword)
    {
        Assert.False(CredentialsRequestValidator.TryValidate(new() { UserName = userName, Password = password }, out var credentials, out var errors));
        Assert.Null(credentials);
        Assert.Equal(invalidName, errors.ContainsKey("userName"));
        Assert.Equal(invalidPassword, errors.ContainsKey("password"));
        if (invalidName) Assert.Equal(["invalid_username"], errors["userName"]);
        if (invalidPassword) Assert.Equal(["invalid_password"], errors["password"]);
    }

    [Fact]
    public void Oversized_inputs_are_rejected_by_the_value_object_and_http_password_bound()
    {
        Assert.False(CredentialsRequestValidator.TryValidate(new()
        {
            UserName = new string('a', 257),
            Password = new string('b', 129)
        }, out var credentials, out var errors));
        Assert.Null(credentials);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Maximum_inputs_are_accepted_without_trimming_or_normalizing()
    {
        var name = new string('A', 256);
        var password = new string(' ', 128);
        Assert.True(CredentialsRequestValidator.TryValidate(new() { UserName = name, Password = password }, out var credentials, out var errors));
        Assert.Empty(errors);
        Assert.Equal(name, credentials!.UserName.Value);
        Assert.Equal(password, credentials.Password);
    }

    [Fact]
    public void Request_validation_does_not_duplicate_identity_registration_or_password_policy()
    {
        Assert.True(CredentialsRequestValidator.TryValidate(new() { UserName = " Alice! ", Password = "weak" }, out var credentials, out var errors));
        Assert.Empty(errors);
        Assert.Equal(" Alice! ", credentials!.UserName.Value);
        Assert.Equal("weak", credentials.Password);
        Assert.DoesNotContain("weak", credentials.ToString());
    }
}
