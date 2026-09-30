using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

internal sealed class ScoreDisplay
{
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
                if (evt.AwardedScore && age < 2)
                {
                    scoreMessages.Add(
                        new ScoreMessage
                        {
                            EventId = evt.Id,
                            Tick = evt.Tick,
                            Player = evt.Other,
                            Text = "+" + Math.Max(1, (int)evt.Strength.ToFloat()),
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
        world.Winner >= 0
        && (world.Rules.TeamMode ? player.Team == world.Players[world.Winner].Team : player.Slot == world.Winner);

    public static IEnumerable<PlayerState> Players(World world) =>
        world.Rules.TeamMode ? world.Players.GroupBy(p => p.Team).Select(g => g.First()) : world.Players;

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
        bool reset = hudRound != world.RoundNumber || roundScoreLayout != roundWins;
        if (reset)
        {
            hudRound = world.RoundNumber;
            roundScoreLayout = roundWins;
            hudSorted = roundWins;
            Array.Clear(hudScores);
        }

        int Score(PlayerState p) =>
            roundWins ? p.RoundWins - (Winner(world, p) && elapsed < (world.IsShowdown ? .05f : 1f) ? 1 : 0) : p.Score;
        var players = Players(world).ToArray();
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
                    new Vector2(
                        10 * sprite.PixelsPerUnit / sprite.RectWidth,
                        10 * sprite.PixelsPerUnit / sprite.RectHeight
                    ),
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
            if (roundWins)
            {
                float start = world.IsShowdown ? .05f : 1;
                if (Winner(world, p) && elapsed >= start && elapsed < start + (world.IsShowdown ? 10 : 2))
                {
                    if (world.IsShowdown)
                    {
                        text = "WINNER!";
                    }

                    color = EffectAnimation.ScoreFlash(time, color);
                }
            }
            else
            {
                var message = scoreMessages.LastOrDefault(m =>
                    world.Rules.TeamMode ? world.Players[m.Player].Team == p.Team : m.Player == p.Slot
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
