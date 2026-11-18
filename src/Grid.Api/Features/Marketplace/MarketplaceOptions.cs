using Microsoft.Extensions.Options;

namespace Grid.Api.Features.Marketplace;

public sealed class MarketplaceOptions
{
    public const string Section = "Marketplace";

    /// <summary>Lets customers book. Off, providers can still go online; they just get nothing.</summary>
    public bool Enabled { get; set; }

    /// <summary>GRID's share of what the customer pays, fixed onto each job at acceptance.</summary>
    public decimal FeePercent { get; set; } = 15;

    /// <summary>How many providers are offered a request at once.</summary>
    public int OffersPerRound { get; set; } = 3;

    /// <summary>How long a provider has to answer.</summary>
    public int OfferMinutes { get; set; } = 10;

    /// <summary>Rounds before a request is left for Ops as <c>Unfilled</c>.</summary>
    public int MaxRounds { get; set; } = 4;

    /// <summary>Wait before trying again when a round found nobody.</summary>
    public int RetryMinutes { get; set; } = 3;

    /// <summary>Travel radius for providers who haven't set one.</summary>
    public decimal DefaultTravelRadiusKm { get; set; } = 10;

    /// <summary>A visit blocks other visits this long either side of it.</summary>
    public int VisitGapMinutes { get; set; } = 120;

    /// <summary>The earliest a booking can start, counted from now.</summary>
    public int MinLeadMinutes { get; set; } = 60;

    /// <summary>How far ahead a booking can be made.</summary>
    public int MaxLeadDays { get; set; } = 30;

    /// <summary>A finished job the customer hasn't confirmed is confirmed for them after this long.</summary>
    public int AutoConfirmHours { get; set; } = 24;

    /// <summary><c>Hubtel</c> prompts the customer's wallet; <c>Simulated</c> marks every booking paid. Production refuses <c>Simulated</c>.</summary>
    public ServicePayments.ServicePaymentMode PaymentMode { get; set; } = ServicePayments.ServicePaymentMode.Hubtel;

    /// <summary>How long a customer has to approve the MoMo prompt before the booking is given up.</summary>
    public int PaymentTimeoutMinutes { get; set; } = 15;

    public bool RunWorker { get; set; } = true;
    public int PollSeconds { get; set; } = 15;
}

public sealed class MarketplaceOptionsValidator : IValidateOptions<MarketplaceOptions>
{
    public ValidateOptionsResult Validate(string? name, MarketplaceOptions o)
    {
        var failures = new List<string>();
        if (o.FeePercent is < 0 or >= 100) failures.Add("Marketplace:FeePercent must be between 0 and 100.");
        if (o.OffersPerRound < 1) failures.Add("Marketplace:OffersPerRound must be at least 1.");
        if (o.OfferMinutes < 1) failures.Add("Marketplace:OfferMinutes must be at least 1.");
        if (o.MaxRounds < 1) failures.Add("Marketplace:MaxRounds must be at least 1.");
        if (o.RetryMinutes < 1) failures.Add("Marketplace:RetryMinutes must be at least 1.");
        if (o.DefaultTravelRadiusKm <= 0) failures.Add("Marketplace:DefaultTravelRadiusKm must be positive.");
        if (o.AutoConfirmHours < 1) failures.Add("Marketplace:AutoConfirmHours must be at least 1.");
        if (o.PollSeconds < 1) failures.Add("Marketplace:PollSeconds must be at least 1.");
        if (o.PaymentTimeoutMinutes < 1) failures.Add("Marketplace:PaymentTimeoutMinutes must be at least 1.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>The phone numbers that may use <c>/ops</c>: reviewing applications, dispatch and payouts.</summary>
public sealed class OpsOptions
{
    public const string Section = "Ops";

    public List<string> Operators { get; set; } = [];
}
