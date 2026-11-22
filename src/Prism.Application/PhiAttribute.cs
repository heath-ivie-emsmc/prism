namespace Prism.Application;

/// <summary>
/// Marks a response property that can carry PHI: a claim reference, a service date, a payer, a vehicle, an amount owed or a denial reason.
/// A route that returns a type with this attribute needs a PHI permission and an audit record, and is listed in the PHI matrix test.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PhiAttribute : Attribute;
