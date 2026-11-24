using Prism.Application.Metrics;

namespace Prism.Application.Insights;

/// <summary>What a Finding says about the change. <see cref="Info"/> is for a change that helped, so it is not flagged as a worry.</summary>
public enum FindingLabel
{
    /// <summary>The change comes from something that will not repeat, so cash is expected to return to its usual level.</summary>
    Temporary,

    /// <summary>The change comes from something that has held for more than a month and is expected to continue.</summary>
    Structural,

    /// <summary>The change is worth keeping an eye on and has not held long enough to call it lasting.</summary>
    Watch,

    /// <summary>The change helped or is neutral.</summary>
    Info,
}

/// <summary>
/// What a Finding's dollar impact is measured on. A <see cref="PostedCash"/> Finding is part of why the cash posted this month differs from last month
/// and is a Driver. An <see cref="ExpectedEffect"/> Finding is what a change is expected to do to cash as claims are paid, so it is not part of this
/// month's posted change and is never added to the Drivers.
/// </summary>
public enum FindingBasis
{
    PostedCash,
    ExpectedEffect,

    /// <summary>
    /// Another cut of the same posted change (by Reporting Division when the change is already explained by aircraft type). It is true on its own, but it overlaps a
    /// Driver, so it is shown beside the Drivers and never added to them.
    /// </summary>
    AlternativeCut,
}

/// <summary>
/// One computed statement about why a Metric moved. <paramref name="Impact"/> is in USD and signed: negative lowered cash. A Finding is built from
/// Metrics, carries counts and shares only, and never has patient or claim detail.
/// </summary>
public abstract record Finding(FindingLabel Label, FindingBasis Basis, decimal Impact);

/// <summary>
/// Payments on older transports ran above the usual level in one of the two months. <paramref name="Impact"/> is the excess this month minus the excess
/// last month, so a one-off batch last month shows as a negative impact this month. The excess figures are what the posted cash and the transports
/// counted in it are corrected by, so the volume analysis does not count the same batch twice.
/// </summary>
public sealed record AgedPaymentsFinding(
    decimal Impact,
    ApPeriod InflatedPeriod,
    decimal ExcessThisMonth,
    decimal ExcessPriorMonth,
    decimal ExcessTransportsThisMonth,
    decimal ExcessTransportsPriorMonth,
    int BatchTransports)
    : Finding(FindingLabel.Temporary, FindingBasis.PostedCash, Impact);

/// <summary>More or fewer transports had cash posted, priced at last month's cash per transport.</summary>
public sealed record VolumeFinding(FindingLabel Label, decimal Impact, decimal TransportsFrom, decimal TransportsTo)
    : Finding(Label, FindingBasis.PostedCash, Impact);

/// <summary>
/// The payer mix moved, which is expected to change cash as those claims are paid. <paramref name="PayerKey"/> is the payer group whose share rose most
/// and <paramref name="ShareFrom"/> to <paramref name="ShareTo"/> are its shares of transports (0 to 1). <paramref name="Impact"/> is the effect of all
/// the shifts together.
/// </summary>
public sealed record PayerMixFinding(FindingLabel Label, decimal Impact, string PayerKey, string PayerName, decimal ShareFrom, decimal ShareTo)
    : Finding(Label, FindingBasis.ExpectedEffect, Impact);

/// <summary>
/// Where the transports with cash posted moved between aircraft types (or levels of service) or between Reporting Divisions, valued at what each one earned last month.
/// <paramref name="Impact"/> is the mix effect on posted cash: the sum over segments of (transports now minus last month's transports scaled to this month's total) times that
/// segment's cash per transport last month. <paramref name="SegmentKey"/> is the segment with the largest effect and <paramref name="ShareFrom"/> to <paramref name="ShareTo"/> its
/// share of the transports with cash posted (0 to 1). <paramref name="SegmentRate"/> and <paramref name="AverageRate"/> are its cash per transport last month and the average of all
/// segments. An aircraft-mix Finding is a Driver. A base-mix Finding is a Driver only when no aircraft mix explains the change, and is otherwise shown beside the Drivers.
/// </summary>
public abstract record SegmentMixFinding(
    FindingLabel Label,
    FindingBasis Basis,
    decimal Impact,
    string SegmentKey,
    string SegmentName,
    decimal ShareFrom,
    decimal ShareTo,
    decimal SegmentRate,
    decimal AverageRate)
    : Finding(Label, Basis, Impact);

/// <summary>Transports moved between aircraft types (air) or levels of service (ground).</summary>
public sealed record AircraftMixFinding(FindingLabel Label, decimal Impact, string SegmentKey, string SegmentName, decimal ShareFrom, decimal ShareTo, decimal SegmentRate, decimal AverageRate)
    : SegmentMixFinding(Label, FindingBasis.PostedCash, Impact, SegmentKey, SegmentName, ShareFrom, ShareTo, SegmentRate, AverageRate);

/// <summary>Transports moved between Reporting Divisions. <see cref="Finding.Basis"/> says whether it is a Driver or shown beside them.</summary>
public sealed record BaseMixFinding(FindingLabel Label, FindingBasis Basis, decimal Impact, string SegmentKey, string SegmentName, decimal ShareFrom, decimal ShareTo, decimal SegmentRate, decimal AverageRate)
    : SegmentMixFinding(Label, Basis, Impact, SegmentKey, SegmentName, ShareFrom, ShareTo, SegmentRate, AverageRate);
