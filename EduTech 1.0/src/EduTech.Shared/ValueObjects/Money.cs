namespace EduTech.Shared.ValueObjects;

/// <summary>
/// Money (EDD-003): an amount + currency, immutable and validated — no more raw decimals in new
/// Finance code. Wire format stays a plain decimal (NGN implied) until the Finance API versioning
/// moment, so existing DTOs/columns are untouched; convert at the boundary with
/// <see cref="Naira"/> / <see cref="Amount"/>.
/// </summary>
public readonly record struct Money
{
    public const string DefaultCurrency = "NGN";

    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = DefaultCurrency)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Money cannot be negative.");
        }
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a 3-letter ISO code.", nameof(currency));
        }

        Amount = decimal.Round(amount, 2, MidpointRounding.ToEven);
        Currency = currency.ToUpperInvariant();
    }

    public static Money Naira(decimal amount) => new Money(amount);
    public static Money Zero { get; } = new Money(0m);

    public bool IsZero => Amount == 0m;

    public static Money operator +(Money a, Money b) => new Money(a.Amount + b.Amount, SameCurrency(a, b));

    /// <summary>Subtraction floors at zero — balances never go negative (overpay is rejected upstream).</summary>
    public static Money operator -(Money a, Money b) =>
        new Money(Math.Max(0m, a.Amount - b.Amount), SameCurrency(a, b));

    public static bool operator >(Money a, Money b) { SameCurrency(a, b); return a.Amount > b.Amount; }
    public static bool operator <(Money a, Money b) { SameCurrency(a, b); return a.Amount < b.Amount; }
    public static bool operator >=(Money a, Money b) { SameCurrency(a, b); return a.Amount >= b.Amount; }
    public static bool operator <=(Money a, Money b) { SameCurrency(a, b); return a.Amount <= b.Amount; }

    public override string ToString() => $"{Currency} {Amount:N2}";

    private static string SameCurrency(Money a, Money b) =>
        a.Currency == b.Currency
            ? a.Currency
            : throw new InvalidOperationException($"Currency mismatch: {a.Currency} vs {b.Currency}.");
}
