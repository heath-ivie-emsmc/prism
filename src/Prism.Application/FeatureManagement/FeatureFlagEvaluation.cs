using System.Text.Json;

namespace Prism.Application.FeatureManagement;

/// <summary>Evaluates one feature value for a Provider and user. Pure: it reads nothing but its two arguments, and any malformed setting means off.</summary>
public static class FeatureFlagEvaluation
{
    private const string ProviderAllowlistSetting = "provider_allowlist";
    private const string UserAllowlistSetting = "user_allowlist";
    private const string TargetingStrategySetting = "targeting_strategy";
    private const string AndStrategy = "and";
    private const string OrStrategy = "or";

    public static bool EvaluateEnabled(FeatureValue featureValue, FeatureContext context)
    {
        if (!featureValue.Enabled)
        {
            return false;
        }

        if (!TryResolveTargetingStrategy(featureValue, out bool requireAll))
        {
            return false;
        }

        var results = new List<bool>(capacity: 2);

        AllowlistEvaluation providerResult = EvaluateAllowlist(
            featureValue.Settings,
            ProviderAllowlistSetting,
            context.ProviderId);
        if (providerResult == AllowlistEvaluation.Invalid)
        {
            return false;
        }

        if (providerResult != AllowlistEvaluation.NotConfigured)
        {
            results.Add(providerResult == AllowlistEvaluation.Matched);
        }

        AllowlistEvaluation userResult = EvaluateAllowlist(
            featureValue.Settings,
            UserAllowlistSetting,
            context.UserId);
        if (userResult == AllowlistEvaluation.Invalid)
        {
            return false;
        }

        if (userResult != AllowlistEvaluation.NotConfigured)
        {
            results.Add(userResult == AllowlistEvaluation.Matched);
        }

        if (results.Count == 0)
        {
            return true;
        }

        return requireAll ? results.All(static result => result) : results.Any(static result => result);
    }

    private static bool TryResolveTargetingStrategy(FeatureValue featureValue, out bool requireAll)
    {
        requireAll = false;

        if (!featureValue.Settings.TryGetValue(
                TargetingStrategySetting,
                out JsonElement strategyNode))
        {
            return true;
        }

        if (strategyNode.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? strategy = strategyNode.GetString();
        if (string.Equals(strategy, AndStrategy, StringComparison.OrdinalIgnoreCase))
        {
            requireAll = true;
            return true;
        }

        if (string.Equals(strategy, OrStrategy, StringComparison.OrdinalIgnoreCase))
        {
            requireAll = false;
            return true;
        }

        return false;
    }

    private static AllowlistEvaluation EvaluateAllowlist(
        IReadOnlyDictionary<string, JsonElement> settings,
        string key,
        long? contextId)
    {
        if (!settings.TryGetValue(key, out JsonElement allowlistNode))
        {
            return AllowlistEvaluation.NotConfigured;
        }

        if (allowlistNode.ValueKind != JsonValueKind.Array)
        {
            return AllowlistEvaluation.Invalid;
        }

        if (!contextId.HasValue)
        {
            return AllowlistEvaluation.NotMatched;
        }

        long expectedId = contextId.Value;
        bool matched = false;
        foreach (JsonElement item in allowlistNode.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt64(out long configuredId))
            {
                return AllowlistEvaluation.Invalid;
            }

            if (configuredId == expectedId)
            {
                matched = true;
            }
        }

        return matched ? AllowlistEvaluation.Matched : AllowlistEvaluation.NotMatched;
    }

    private enum AllowlistEvaluation
    {
        NotConfigured = 0,
        Matched = 1,
        NotMatched = 2,
        Invalid = 3
    }
}
