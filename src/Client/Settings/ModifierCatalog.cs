using FrogSmashers.Core;

namespace FrogSmashers.Client;

internal sealed record ModifierDefinition(
    string Id,
    string Label,
    string ValueSample,
    Func<GameModifiers, string> Value,
    Func<GameModifiers, int, GameModifiers> Change,
    Func<GameRules, string?>? DisabledReason = null,
    Func<GameRules, bool>? Disabled = null
);

internal static class ModifierCatalog
{
    private static string OnOff(bool enabled) => enabled ? "ON" : "OFF";

    private static bool BodyDisabled(GameRules rules) => !rules.Modifiers.BodyBouncing;

    private static string? PointsOnly(GameRules rules) => rules.Scoring != ScoringMode.Points ? "POINTS ONLY" : null;

    private static bool FliesDisabled(GameRules rules) => !rules.Modifiers.FlyEnabled;

    public static IReadOnlyList<ModifierDefinition> All { get; } =
        Array.AsReadOnly<ModifierDefinition>([
            new(
                "physics-fixes",
                "PHYSICS FIXES",
                "OFF",
                m => OnOff(m.PhysicsFixes),
                (m, _) => m with { PhysicsFixes = !m.PhysicsFixes }
            ),
            new(
                "body-bouncing",
                "BODY BOUNCING",
                "OFF",
                m => OnOff(m.BodyBouncing),
                (m, _) => m with { BodyBouncing = !m.BodyBouncing }
            ),
            new(
                "bounce-recovery",
                "BOUNCE UNTIL",
                "RECOVERY",
                m => m.BounceBeforeRecoveryOnly ? "APEX" : "RECOVERY",
                (m, _) => m with { BounceBeforeRecoveryOnly = !m.BounceBeforeRecoveryOnly },
                Disabled: BodyDisabled
            ),
            new(
                "redirect-bounces",
                "REDIRECT BOUNCES",
                "OFF",
                m => OnOff(m.RedirectBounces),
                (m, _) => m with { RedirectBounces = !m.RedirectBounces },
                Disabled: BodyDisabled
            ),
            new(
                "suicide-penalty",
                "SUICIDE PENALTY",
                "OFF",
                m => OnOff(m.SuicidePenalty),
                (m, _) => m with { SuicidePenalty = !m.SuicidePenalty },
                PointsOnly
            ),
            new(
                "match-scoring",
                "CUMULATIVE POINTS",
                "OFF",
                m => OnOff(m.MatchScoring == MatchScoring.CumulativePoints),
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
            new("flies", "FLIES", "OFF", m => OnOff(m.FlyEnabled), (m, _) => m with { FlyEnabled = !m.FlyEnabled }),
            new(
                "fly-min",
                "FLY DELAY MIN",
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
                Disabled: FliesDisabled
            ),
            new(
                "fly-max",
                "FLY DELAY MAX",
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
                Disabled: FliesDisabled
            ),
        ]);
}
