using FluentAssertions;
using SpatialAI.Api.Auth;
using Xunit;

namespace SpatialAI.Tests;

public class AuthTests
{
    private static AuthRepository NewAuth() =>
        new(Path.Combine(Path.GetTempPath(), "spatialai-tests", Guid.NewGuid().ToString("N"), "app.db"));

    [Fact]
    public void Api_token_is_idempotent_and_resolves_to_its_owner()
    {
        var auth = NewAuth();
        var user = auth.GetOrCreateVerifiedUser("a@x.com", "A");

        var t1 = auth.GetOrCreateApiToken(user.Id);
        var t2 = auth.GetOrCreateApiToken(user.Id);
        t1.Should().StartWith("mcp_");
        t2.Should().Be(t1);                         // stable personal token, not regenerated each call

        var found = auth.FindByApiToken(t1);
        found.Should().NotBeNull();
        found!.Id.Should().Be(user.Id);
        found.Email.Should().Be("a@x.com");
    }

    [Fact]
    public void Api_tokens_are_distinct_per_user_and_isolate_lookups()
    {
        var auth = NewAuth();
        var a = auth.GetOrCreateVerifiedUser("a@x.com", "A");
        var b = auth.GetOrCreateVerifiedUser("b@x.com", "B");

        var ta = auth.GetOrCreateApiToken(a.Id);
        var tb = auth.GetOrCreateApiToken(b.Id);
        ta.Should().NotBe(tb);
        auth.FindByApiToken(ta)!.Id.Should().Be(a.Id);
        auth.FindByApiToken(tb)!.Id.Should().Be(b.Id);
    }

    [Fact]
    public void Unknown_or_blank_token_resolves_to_null()
    {
        var auth = NewAuth();
        auth.FindByApiToken("mcp_does_not_exist").Should().BeNull();
        auth.FindByApiToken("").Should().BeNull();
        auth.FindByApiToken(null).Should().BeNull();
        auth.FindByApiToken("   ").Should().BeNull();
    }
}
