using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SpatialAI.Api;
using SpatialAI.Api.Auth;
using Xunit;

namespace SpatialAI.Tests;

public class BudgetTests
{
    private static AuthRepository NewAuth() =>
        new(Path.Combine(Path.GetTempPath(), "spatialai-tests", Guid.NewGuid().ToString("N"), "app.db"));

    private static BudgetStore Make(AuthRepository auth, int perUser, int global)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Budget:MessagesPerUser"] = perUser.ToString(),
                ["Budget:GlobalMessageCeiling"] = global.ToString(),
            })
            .Build();
        return new BudgetStore(config, auth);
    }

    // The budget is keyed by the account id, which is stable per email (idempotent).
    private static string User(AuthRepository auth, string email) => auth.GetOrCreateVerifiedUser(email, email).Id;

    [Fact]
    public void Per_user_cap_blocks_after_limit_but_isolates_users()
    {
        var auth = NewAuth();
        var b = Make(auth, perUser: 3, global: 1000);
        var u1 = User(auth, "u1@x.com");

        for (var i = 0; i < 3; i++)
            b.TryConsume(u1, out _).Should().BeTrue();

        b.TryConsume(u1, out var remaining).Should().BeFalse();
        remaining.Should().Be(0);
        b.Remaining(u1).Should().Be(0);

        // A different user has their own independent budget.
        b.TryConsume(User(auth, "u2@x.com"), out var r2).Should().BeTrue();
        r2.Should().Be(2);
    }

    [Fact]
    public void Global_ceiling_blocks_every_user_as_the_backstop()
    {
        var auth = NewAuth();
        var b = Make(auth, perUser: 100, global: 2);

        b.TryConsume(User(auth, "u1@x.com"), out _).Should().BeTrue();
        b.TryConsume(User(auth, "u2@x.com"), out _).Should().BeTrue();

        // Global ceiling (2) reached — even a brand-new user is blocked.
        b.TryConsume(User(auth, "u3@x.com"), out _).Should().BeFalse();
    }

    [Fact]
    public void Remaining_counts_down_per_message()
    {
        var auth = NewAuth();
        var b = Make(auth, perUser: 5, global: 1000);
        var x = User(auth, "x@x.com");

        b.Remaining(x).Should().Be(5);
        b.TryConsume(x, out var rem).Should().BeTrue();
        rem.Should().Be(4);
        b.Remaining(x).Should().Be(4);
    }

    [Fact]
    public void Anonymous_user_without_a_registered_row_still_gets_their_allowance()
    {
        var auth = NewAuth();
        var b = Make(auth, perUser: 3, global: 1000);
        var anon = Guid.NewGuid().ToString("N");   // an open/dev-mode uid that was never registered

        b.Remaining(anon).Should().Be(3);
        b.TryConsume(anon, out var rem).Should().BeTrue();   // used to fail: no users row → 0 rows updated
        rem.Should().Be(2);
        b.TryConsume(anon, out _).Should().BeTrue();
        b.TryConsume(anon, out _).Should().BeTrue();
        b.TryConsume(anon, out var last).Should().BeFalse(); // per-user cap still enforced
        last.Should().Be(0);
    }

    [Fact]
    public void Usage_persists_across_restart_and_re_login()
    {
        var auth = NewAuth();
        var b1 = Make(auth, perUser: 5, global: 1000);
        var id = User(auth, "mehdi@x.com");
        b1.TryConsume(id, out _); b1.TryConsume(id, out _); b1.TryConsume(id, out _); // 3 used → 2 left

        // "Restart": a fresh BudgetStore over the SAME persisted db resumes the count (not reset to 5).
        var b2 = Make(auth, perUser: 5, global: 1000);
        b2.Remaining(id).Should().Be(2);

        // "Re-login": the same email resolves to the same account id → same count (no free reset).
        var sameId = User(auth, "MEHDI@x.com");   // idempotent + case-insensitive
        sameId.Should().Be(id);
        b2.Remaining(sameId).Should().Be(2);

        // The restarted store's global tally was seeded from the db (3 already spent).
        var (globalUsed, _, _, _, _) = b2.Stats();
        globalUsed.Should().Be(3);
    }
}
