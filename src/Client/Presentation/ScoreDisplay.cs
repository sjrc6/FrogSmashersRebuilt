using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

internal sealed class ScoreDisplay
{
    internal static Vector2 IconPixelScale(SpriteData sprite, float pixelsPerUnit) =>
        new(10 * pixelsPerUnit / sprite.RectWidth, 10 * pixelsPerUnit / sprite.RectHeight);

    private readonly Assets assets;
    private readonly SpriteCanvas canvas;
    private readonly GameCamera cameraController;
    private SpriteBatch batch => canvas.Batch;
    public IReadOnlyList<ScoreMessage> Messages => scoreMessages;
    private readonly List<ScoreMessage> scoreMessages = new();
    private readonly List<(long EventId, long Tick, int Player)> scoreCharacterResets = new();
    private readonly Vector2[] scorePositions = new Vector2[8];
    private readonly int[] hudScores = new int[8];
    private int hudRound = -1;
    private bool hudSorted;
    private bool roundScoreLayout;

    public ScoreDisplay(Assets assets, SpriteCanvas canvas, GameCamera camera)
    {
        this.assets = assets;
        this.canvas = canvas;
        cameraController = camera;
    }

    public void Reset()
    {
        scoreMessages.Clear();
        scoreCharacterResets.Clear();
        hudRound = -1;
    }

    public void Update(float dt)
    {
        scoreMessages.RemoveAll(message => message.Age >= message.Life);
        foreach (var message in scoreMessages)
        {
            message.Age += dt;
        }

        scoreCharacterResets.RemoveAll(reset =>
            !scoreMessages.Any(message => message.Player == reset.Player && message.EventId <= reset.EventId)
        );
    }

    public void Rewind(long fromTick)
    {
        scoreMessages.RemoveAll(message => message.Tick >= fromTick);
        scoreCharacterResets.RemoveAll(reset => reset.Tick >= fromTick);
    }

    public void Consume(SimulationEvent evt, float age)
    {
        switch (evt.Kind)
        {
            case SimulationEventKind.Spawn:
                scoreCharacterResets.Add((evt.Id, evt.Tick, evt.Player));
                break;
            case SimulationEventKind.Death:
                scoreCharacterResets.Add((evt.Id, evt.Tick, evt.Player));
                hudSorted = true;
                if (evt.ScoreDelta != 0 && age < 2)
                {
                    scoreMessages.Add(
                        new ScoreMessage
                        {
                            EventId = evt.Id,
                            Tick = evt.Tick,
                            Player = evt.ScoreDelta < 0 ? evt.Player : evt.Other,
                            Text = evt.ScoreDelta.ToString("+0;-0;0"),
                            Life = 2,
                            Age = age,
                        }
                    );
                }
                break;
            case SimulationEventKind.RoundWin:
                if (age < 5)
                {
                    scoreMessages.Add(
                        new ScoreMessage
                        {
                            EventId = evt.Id,
                            Tick = evt.Tick,
                            Player = evt.Player,
                            Text = "WINNER ! ! !",
                            Overhead = "WIN!",
                            Life = 5,
                            Age = age,
                        }
                    );
                }
                break;
        }
    }

    public ScoreMessage? OverheadMessage(int slot) =>
        scoreMessages.LastOrDefault(message =>
            message.Player == slot
            && message.Age < message.Life
            && !scoreCharacterResets.Any(reset => reset.Player == slot && reset.EventId >= message.EventId)
        );

    private static bool Winner(World world, PlayerState player) =>
        world.Match.Winner >= 0
        && (
            world.Rules.UsesTeams
                ? player.Team == world.Players[world.Match.Winner].Team
                : player.Slot == world.Match.Winner
        );

    public static IEnumerable<PlayerState> Players(World world) =>
        world.Rules.UsesTeams ? world.Players.GroupBy(p => p.Team).Select(g => g.First()) : world.Players;

    public void Draw(
        World world,
        bool roundWins,
        float elapsed,
        float time,
        float frameSeconds,
        bool drawIcons = true,
        bool drawText = true,
        bool updateLayout = true
    )
    {
        bool reset = hudRound != world.Match.RoundNumber || roundScoreLayout != roundWins;
        if (reset)
        {
            hudRound = world.Match.RoundNumber;
            roundScoreLayout = roundWins;
            hudSorted = roundWins;
            Array.Clear(hudScores);
        }

        int Score(PlayerState p) =>
            roundWins
                ? world.Match.Players[p.Slot].TotalScore
                    - (
                        elapsed < (world.Match.IsShowdown ? .05f : 1f)
                            ? world.Match.RoundContribution(world.Rules, p.Slot)
                            : 0
                    )
            : world.Rules.Scoring == ScoringMode.Stocks ? world.Match.Players[p.Slot].Stocks
            : world.Match.Players[p.Slot].Score;
        bool individualStocks = !roundWins && world.Rules.Scoring == ScoringMode.Stocks;
        var players = individualStocks ? world.Players.ToArray() : Players(world).ToArray();
        foreach (var p in players)
        {
            if (hudScores[p.Slot] != Score(p))
            {
                hudSorted = true;
            }

            hudScores[p.Slot] = Score(p);
        }

        if (hudSorted)
        {
            players = players.OrderByDescending(Score).ThenByDescending(p => p.Slot).ToArray();
        }

        int rows = players.Length > 4 ? (players.Length + 1) / 2 : players.Length;
        if (updateLayout)
        {
            for (int i = 0; i < players.Length; i++)
            {
                var position = new Vector2(
                    players.Length > 4 ? (i / rows == 0 ? 2 : -2) : 0,
                    (roundWins ? 4.5f : 18) - i % rows * 2
                );
                int slot = players[i].Slot;
                scorePositions[slot] = reset
                    ? position
                    : Vector2.Lerp(scorePositions[slot], position, Math.Clamp(frameSeconds * 3, 0, 1));
            }
        }

        if (drawIcons)
        {
            canvas.Begin(1);
            foreach (var p in players)
            {
                var id = assets.Frame("idle", 1);
                if (id == null)
                {
                    continue;
                }

                var sprite = assets.Data.Sprites[id];
                canvas.DrawSprite(
                    id,
                    scorePositions[p.Slot] + new Vector2(-1.08f, -2),
                    PlayerPalette.For(world, p.Slot),
                    IconPixelScale(sprite, sprite.PixelsPerUnit),
                    0,
                    true
                );
            }

            canvas.End();
        }

        if (!drawText)
        {
            return;
        }

        canvas.BeginFont();

        foreach (var p in players)
        {
            string text = Score(p).ToString();
            var color = PlayerPalette.For(world, p.Slot);
            if (individualStocks)
            {
                var participation = world.Match.Players[p.Slot].Participation;
                text = $"P{p.Slot + 1} " + (participation == Participation.Eliminated ? "OUT" : text);
                if (participation is Participation.Waiting or Participation.Eliminated)
                    color *= .5f;
            }
            if (roundWins)
            {
                float start = world.Match.IsShowdown ? .05f : 1;
                if (Winner(world, p) && elapsed >= start && elapsed < start + (world.Match.IsShowdown ? 10 : 2))
                {
                    if (world.Match.IsShowdown)
                    {
                        text = "WINNER!";
                    }

                    color = EffectAnimation.ScoreFlash(time, color);
                }
            }
            else
            {
                var message = scoreMessages.LastOrDefault(m =>
                    world.Rules.UsesTeams && !individualStocks
                        ? world.Players[m.Player].Team == p.Team
                        : m.Player == p.Slot
                );
                if (message != null)
                {
                    text = message.Text;
                    color = EffectAnimation.ScoreFlash(time, color);
                }
            }

            float scale = cameraController.PixelsPerUnit / 20;
            var location = cameraController.ToScreen(scorePositions[p.Slot] + new Vector2(.09f, -2.45f));
            assets.ScoreFont.DrawCenteredVertical(batch, text, location, color, scale);
        }

        canvas.End();
    }

    internal sealed class ScoreMessage
    {
        public long EventId;
        public long Tick;
        public int Player;
        public string Text = "";
        public string? Overhead;
        public float Age;
        public float Life;
    }
}
