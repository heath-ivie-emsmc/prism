using Prism.Application.Attention;

namespace Prism.Tests.Attention;

public sealed class AttentionCatalogTests
{
    [Fact]
    public void Checks_have_unique_ordered_snake_case_keys_names_and_descriptions()
    {
        Assert.Equal(AttentionCatalog.Checks.Count, AttentionCatalog.Checks.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(AttentionCatalog.Checks.OrderBy(c => c.Order).Select(c => c.Key), AttentionCatalog.Checks.Select(c => c.Key));
        Assert.All(AttentionCatalog.Checks, check =>
        {
            Assert.Matches("^[a-z][a-z_]{1,48}$", check.Key);
            Assert.False(string.IsNullOrWhiteSpace(check.Name));
            Assert.False(string.IsNullOrWhiteSpace(check.Description));
        });
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("needs_attention")]
    [InlineData("all")]
    public void A_known_key_is_found(string key)
    {
        Assert.Equal(key, AttentionCatalog.Find(key)?.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("DENIED")]
    [InlineData("denied ")]
    [InlineData("denied;drop table")]
    public void Anything_else_is_not_found(string? key)
    {
        Assert.Null(AttentionCatalog.Find(key));
    }

    [Fact]
    public void Every_predicate_has_a_check_and_the_tab_order_is_denied_needs_attention_all()
    {
        Assert.Equal(["denied", "needs_attention", "all"], AttentionCatalog.Checks.Select(c => c.Key));
        Assert.Equal(Enum.GetValues<AttentionPredicate>().Order(), AttentionCatalog.Checks.Select(c => c.Predicate).Order());
    }
}
