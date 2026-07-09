using SpatialAI.Api.Auth;

namespace SpatialAI.Api;

/// <summary>
/// Per-user message budget with a hard global ceiling. Message-based (one /api/chat turn = one message)
/// so it stays intuitive to display ("N of 50 left"); token usage is also accumulated for cost visibility.
/// The per-user count is <b>persisted per account in app.db</b> (via <see cref="AuthRepository"/>) so it
/// survives restarts AND re-logins — a user can't reset their allowance by signing in again or waiting for
/// a redeploy. The global ceiling is the "don't bankrupt me" backstop; it's kept in memory but seeded from
/// the persisted total on startup.
/// </summary>
public sealed class BudgetStore
{
    private readonly int _perUser;
    private readonly int _globalCeiling;
    private readonly AuthRepository _auth;
    private readonly object _gate = new();
    private int _globalUsed;
    private long _inTok, _outTok, _cacheReadTok;

    public BudgetStore(IConfiguration config, AuthRepository auth)
    {
        _auth = auth;
        _perUser = ParseInt(config["Budget:MessagesPerUser"], 50);
        _globalCeiling = ParseInt(config["Budget:GlobalMessageCeiling"], 5000);
        _globalUsed = auth.TotalMessagesUsed();   // resume the global tally across restarts
    }

    public int PerUser => _perUser;

    public int Remaining(string userId) => _auth.RemainingMessages(userId, _perUser);

    /// <summary>Consume one message for the user if both the per-user cap and the global ceiling allow it.</summary>
    public bool TryConsume(string userId, out int remaining) => TryConsume(userId, 1, out remaining);

    /// <summary>
    /// Consume <paramref name="cost"/> messages atomically, only if BOTH the per-user cap (persistent) and
    /// the global ceiling can absorb the whole cost. Used for a chat turn (cost 1) and heavier operations
    /// like a plan import (cost N).
    /// </summary>
    public bool TryConsume(string userId, int cost, out int remaining)
    {
        if (cost < 1) cost = 1;
        lock (_gate)
        {
            if (_globalUsed + cost > _globalCeiling) { remaining = _auth.RemainingMessages(userId, _perUser); return false; }
            if (!_auth.TryConsumeMessages(userId, cost, _perUser, out remaining)) return false; // per-user cap, persisted
            _globalUsed += cost;
            return true;
        }
    }

    public void RecordTokens(string userId, long input, long output, long cacheRead)
    {
        lock (_gate)
        {
            _inTok += input;
            _outTok += output;
            _cacheReadTok += cacheRead;
        }
    }

    /// <summary>Snapshot for diagnostics / an admin view.</summary>
    public (int globalUsed, int globalCeiling, long inputTokens, long outputTokens, long cacheReadTokens) Stats()
    {
        lock (_gate) return (_globalUsed, _globalCeiling, _inTok, _outTok, _cacheReadTok);
    }

    private static int ParseInt(string? s, int dflt) => int.TryParse(s, out var v) && v > 0 ? v : dflt;
}
