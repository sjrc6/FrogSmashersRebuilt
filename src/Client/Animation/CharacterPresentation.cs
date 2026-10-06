using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class CharacterPresentation
{
    private enum State
    {
        Idle,
        Running,
        Jumping,
        Skidding,
        Bounced,
        Charge,
        Attack,
        Recover,
        Wall,
        Tongue,
    }

    private State state;
    private int frame;
    private float counter;
    private float transition;
    private bool wasBurp;
    public string? Sprite;
    public float Rotation;
    public float OffsetY;
    public bool Transitioning;
    public Color Color;
    public readonly FlightPresentation Flight = new();
    public float PowerFlashCounter;
    public int PowerFlashFrame;
    public float LastSkidX;
    public readonly SpriteAnimation TipClock = new();
    public bool TongueVisible;
    public Vector2 TongueOffset;

    public void Update(Assets assets, PlayerState player, World world, float rawDelta)
    {
        float dt = rawDelta * (player.HitstopTicks > 0 ? player.HitstopScale.ToFloat() : 1);
        State next = GetState(player);
        Rotation = 0;
        OffsetY = assets.Data.CharacterOffsetY;
        if (state != next)
        {
            frame = 0;
            counter = 0;
            if (state == State.Skidding)
            {
                transition = .05f;
                Sprite = assets.Frame("skidRecover");
            }
            else if (next == State.Jumping && state == State.Wall)
            {
                transition = .05f;
                Sprite = assets.Frame("wallSlideJumpLaunch");
            }
        }

        state = next;
        Transitioning = false;
        if (transition > 0 && state != State.Bounced)
        {
            transition -= dt;
            Transitioning = true;
            return;
        }

        if (state == State.Bounced)
        {
            transition = 0;
        }

        void Animate(string key, float delay, bool clamp = false, bool raw = false)
        {
            counter += raw ? rawDelta : dt;
            if (counter > delay || frame < 0)
            {
                frame++;
                counter -= delay;
            }

            Sprite = assets.Frame(key, frame, clamp);
        }

        switch (state)
        {
            case State.Idle:
                counter += dt;
                bool winner =
                    world.Match.Phase == MatchPhase.RoundFinished
                    && world.Match.Winner >= 0
                    && (
                        world.Rules.UsesTeams
                            ? player.Team == world.Players[world.Match.Winner].Team
                            : player.Slot == world.Match.Winner
                    );
                if (winner)
                {
                    Sprite = counter < .3f ? assets.Frame("idle") : assets.Frame("win", VictoryFrame(counter));
                }
                else
                {
                    Sprite = assets.Frame(
                        "idle",
                        counter < 1 ? 0
                            : counter % 3 < 2.95f ? 1
                            : 2
                    );
                }

                break;
            case State.Running:
                Animate("run", .04f);
                break;
            case State.Wall:
                Animate("wallSlide", .04f);
                break;
            case State.Charge:
                var suffix = AttackDirection(player);
                Animate(
                    suffix == "Up" ? "attachChargeUp" : "attackCharge" + suffix,
                    MathHelper.Lerp(.2f, .03f, player.ChargeRatio.ToFloat())
                );
                break;
            case State.Attack:
                var direction = AttackDirection(player);
                if (direction == "")
                {
                    Sprite = assets.Frame("attack");
                }
                else
                {
                    Animate("attack" + direction, .05f, true);
                }

                break;
            case State.Recover:
                Animate("attackRecover" + AttackDirection(player), .05f, true);
                break;
            case State.Jumping:
                if (player.VY < 15 && player.PreviousInput.Jump && player.GravityGraceLeft > 0)
                {
                    Sprite = assets.Frame(
                        "somersault",
                        (int)(
                            (1 - player.GravityGraceLeft.ToFloat() / .25f)
                            * (assets.Data.Animations["somersault"].Length)
                        ),
                        true
                    );
                }
                else if (player.VY > 0)
                {
                    if (frame < assets.Data.Animations["jumpLaunch"].Length)
                    {
                        Sprite = assets.Frame("jumpLaunch", frame);
                        counter += dt;
                        if (counter > .05f)
                        {
                            frame++;
                            counter = 0;
                        }
                    }
                    else
                    {
                        Animate("jumpUp", .05f);
                    }
                }
                else
                {
                    Animate("jumpDown", .05f);
                }

                break;
            case State.Skidding:
                if (frame == 0)
                {
                    Sprite = assets.Frame("skidLand");
                    counter += dt;
                    if (counter > .075f)
                    {
                        frame++;
                        counter = 0;
                    }
                }
                else
                {
                    Animate("skid", .05f);
                }

                break;
            case State.Bounced:
                if (player.HitstopTicks > 0 && player.HitstopScale == 0)
                {
                    Animate("impact", .05f, false, true);
                }
                else if (player.HasBounceDodged)
                {
                    Animate("somersault", .05f);
                }
                else
                {
                    Rotation = MathF.Atan2(player.VY.ToFloat(), player.VX.ToFloat()) - MathF.PI / 2;
                    if (!player.CanBounceDodge)
                    {
                        OffsetY = 1;
                        Animate(
                            player.HitsTaken > 4 ? "bouncedFlyingComet"
                                : player.HitsTaken > 2 ? "bouncedFlying"
                                : "bouncedFlyingSpinning",
                            .04f
                        );
                    }
                    else
                    {
                        Animate("bouncedFlyingRecovered", .05f);
                    }
                }

                break;
            case State.Tongue:
                if (player.TonguePhase == TonguePhase.Burping)
                {
                    if (!wasBurp)
                    {
                        frame = 0;
                        counter = 0;
                        wasBurp = true;
                    }

                    Animate("tongueBurp", .05f, true);
                }
                else
                {
                    wasBurp = false;
                    if (player.TonguePhase == TonguePhase.Stunned)
                    {
                        Animate("blush", .05f);
                    }
                    else if (!player.OnGround && player.TongueY < 0)
                    {
                        Animate(player.VY >= 0 ? "tongueDownMovingUp" : "tongueDownMovingDown", .1f, true);
                    }
                    else if (player.TonguePhase == TonguePhase.RetractingHitEnemyTongue)
                    {
                        Animate("tongueRetractStunned", .05f);
                    }
                    else
                    {
                        Animate("tongue", .1f, true);
                    }
                }

                break;
        }
    }

    private static State GetState(PlayerState player)
    {
        return player.Mode switch
        {
            CharacterMode.Bouncing => player.OnGround ? State.Skidding : State.Bounced,
            CharacterMode.Attacking => player.AttackPhase switch
            {
                AttackPhase.Charging => State.Charge,
                AttackPhase.Swing => State.Attack,
                _ => State.Recover,
            },
            CharacterMode.Tongue => State.Tongue,
            _ when player.OnGround => player.VX != Fixed.Zero ? State.Running : State.Idle,
            _ when player.WallSliding => State.Wall,
            _ => State.Jumping,
        };
    }

    private static string AttackDirection(PlayerState player)
    {
        if (player.AttackY == 1 && player.AttackX == 0)
        {
            return "Up";
        }
        if (MathF.Abs(player.AttackX.ToFloat()) > .45f)
        {
            if (player.AttackY.ToFloat() > .45f)
            {
                return "DiagUp";
            }
            if (player.AttackY.ToFloat() < -.45f)
            {
                return "DownForward";
            }
        }
        return player.AttackY == -1 ? "Down" : "";
    }

    private static int VictoryFrame(float elapsed)
    {
        if (elapsed < .4f)
        {
            return 0;
        }
        return (elapsed % 2) switch
        {
            < .9f => 3,
            < 1 => 2,
            < 1.9f => 1,
            _ => 2,
        };
    }
}
