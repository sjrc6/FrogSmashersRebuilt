using FrogSmashers.Core;

namespace FrogSmashers.Client;

internal sealed record ModifierDefinition(
    string Id,
    string Label,
    string Help,
    string ValueSample,
    Func<GameModifiers, string> Value,
    Func<GameModifiers, int, GameModifiers> Change,
    Func<GameRules, string?>? DisabledReason = null
);

internal static class ModifierCatalog
{
    private static string OnOff(bool enabled) => enabled ? "ON" : "OFF";

    private static string? BodyOnly(GameRules rules) =>
        rules.Format != MatchFormat.Ffa ? "FFA ONLY"
        : !rules.Modifiers.BodyBouncing ? "ENABLE BODY BOUNCING"
        : null;

    private static string? PointsOnly(GameRules rules) => rules.Scoring != ScoringMode.Points ? "POINTS ONLY" : null;

    private static string? FlyOnly(GameRules rules) => rules.Modifiers.FlyEnabled ? null : "ENABLE FLIES";

    public static IReadOnlyList<ModifierDefinition> All { get; } =
        Array.AsReadOnly<ModifierDefinition>([
            new(
                "physics-fixes",
                "PHYSICS FIXES",
                "FULL BODY COLLISION, RELIABLE WALL SLIDES, AND TONGUES THAT STOP AT WALLS.",
                "OFF",
                m => OnOff(m.PhysicsFixes),
                (m, _) => m with { PhysicsFixes = !m.PhysicsFixes }
            ),
            new(
                "body-bouncing",
                "BODY BOUNCING",
                "LAUNCHED FROGS KNOCK INTO OTHER FROGS. FFA ONLY.",
                "OFF",
                m => OnOff(m.BodyBouncing),
                (m, _) => m with { BodyBouncing = !m.BodyBouncing },
                rules => rules.Format != MatchFormat.Ffa ? "FFA ONLY" : null
            ),
            new(
                "bounce-recovery",
                "BOUNCE UNTIL",
                "ALLOW BODY HITS ONLY BEFORE THE LAUNCH APEX, OR THROUGHOUT RECOVERY.",
                "RECOVERY",
                m => m.BounceBeforeRecoveryOnly ? "APEX" : "RECOVERY",
                (m, _) => m with { BounceBeforeRecoveryOnly = !m.BounceBeforeRecoveryOnly },
                BodyOnly
            ),
            new(
                "redirect-bounces",
                "REDIRECT BOUNCES",
                "BODY CONTACT ALSO DEFLECTS THE LAUNCHED FROG AWAY FROM ITS TARGET.",
                "OFF",
                m => OnOff(m.RedirectBounces),
                (m, _) => m with { RedirectBounces = !m.RedirectBounces },
                BodyOnly
            ),
            new(
                "suicide-penalty",
                "SUICIDE PENALTY",
                "LOSE ONE POINT FOR AN UNASSISTED DEATH. SCORES CAN GO BELOW ZERO.",
                "OFF",
                m => OnOff(m.SuicidePenalty),
                (m, _) => m with { SuicidePenalty = !m.SuicidePenalty },
                PointsOnly
            ),
            new(
                "match-scoring",
                "MATCH SCORE",
                "WINS COUNTS ROUND VICTORIES. POINTS ADDS EVERY ROUND'S SCORE TO THE FINAL STANDINGS.",
                "POINTS",
                m => m.MatchScoring == MatchScoring.RoundWins ? "WINS" : "POINTS",
                (m, _) =>
                    m with
                    {
                        MatchScoring =
                            m.MatchScoring == MatchScoring.RoundWins
                                ? MatchScoring.CumulativePoints
                                : MatchScoring.RoundWins,
                    },
                PointsOnly
            ),
            new(
                "flies",
                "FLIES",
                "ENABLE POWER FLIES DURING ROUNDS. SHOWDOWN ALWAYS HAS NO FLIES.",
                "OFF",
                m => OnOff(m.FlyEnabled),
                (m, _) => m with { FlyEnabled = !m.FlyEnabled }
            ),
            new(
                "fly-min",
                "FLY DELAY MIN",
                "MINIMUM SECONDS BEFORE A FLY SPAWNS OR RETURNS. EQUAL LIMITS GIVE A FIXED DELAY.",
                "120 S",
                m => m.FlySpawnMinSeconds + " S",
                (m, amount) =>
                    m with
                    {
                        FlySpawnMinSeconds = Math.Clamp(
                            m.FlySpawnMinSeconds + amount,
                            GameModifiers.MinimumFlyDelay,
                            m.FlySpawnMaxSeconds
                        ),
                    },
                FlyOnly
            ),
            new(
                "fly-max",
                "FLY DELAY MAX",
                "MAXIMUM SECONDS BEFORE A FLY SPAWNS OR RETURNS. THE DELAY IS CHOSEN EACH TIME.",
                "120 S",
                m => m.FlySpawnMaxSeconds + " S",
                (m, amount) =>
                    m with
                    {
                        FlySpawnMaxSeconds = Math.Clamp(
                            m.FlySpawnMaxSeconds + amount,
                            m.FlySpawnMinSeconds,
                            GameModifiers.MaximumFlyDelay
                        ),
                    },
                FlyOnly
            ),
            new(
                "podium",
                "PODIUM ARENA",
                "ADD PODIUM TO THE ROTATION. A FULL ROTATION GAINS ONE ROUND; CUSTOM ROUND COUNTS STAY THE SAME.",
                "OFF",
                m => OnOff(m.IncludePodium),
                (m, _) => m with { IncludePodium = !m.IncludePodium }
            ),
        ]);
}
