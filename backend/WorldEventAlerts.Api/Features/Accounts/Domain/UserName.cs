using System.Diagnostics.CodeAnalysis;

namespace WorldEventAlerts.Api.Features.Accounts.Domain;

public sealed record UserName
{
    public const int MaxLength = 256;
    public string Value { get; }

    private UserName(string value) => Value = value;

    public static bool TryCreate(string? value, [NotNullWhen(true)] out UserName? userName)
    {
        // Identity owns its configurable character policy, normalization and uniqueness.
        // These structural invariants also apply to login, without changing its semantics.
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            userName = null;
            return false;
        }

        userName = new UserName(value);
        return true;
    }
}
