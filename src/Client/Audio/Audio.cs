using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace FrogSmashers.Client;

public sealed class Audio : IDisposable
{
    private const float MenuVolume = .30f;
    private readonly Assets assets;
    private readonly AudioSpatializer spatializer;
    private readonly HashSet<long> played = new();
    private readonly Queue<long> order = new();
    private readonly List<(SoundEffectInstance Instance, SoundEffect? OwnedEffect, float BaseVolume)> shots = new();
    private readonly List<(SoundEffectInstance Instance, float BaseVolume)> ambient = new();
    private readonly DynamicFlightLoop?[] flight = new DynamicFlightLoop?[8];
    private readonly int[] flightLevel = Enumerable.Repeat(-1, 8).ToArray();
    private readonly float[] flightOrigin = new float[8];
    private readonly float[] flightVolume = new float[8];
    private string mapId = "";
    private float volume = .65f;
    private bool paused;
    private SoundEffectInstance? menuSound;
    private long menuSoundNumber;
    public Vector3 ListenerPosition { get; set; } = new(0, 0, -10);
    public bool Enabled { get; private set; } = true;

    public float Volume
    {
        get => volume;
        set
        {
            volume = Math.Clamp(value, 0, 1);
            if (menuSound != null)
                menuSound.Volume = MenuVolume * volume;
            foreach (var sound in ambient)
            {
                sound.Instance.Volume = Math.Clamp(sound.BaseVolume * volume, 0, 1);
            }

            foreach (var sound in shots)
            {
                sound.Instance.Volume = Math.Clamp(sound.BaseVolume * volume, 0, 1);
            }

            for (int i = 0; i < flight.Length; i++)
            {
                if (flight[i] != null)
                {
                    flight[i]!.Volume = Math.Clamp(flightVolume[i] * volume, 0, 1);
                }
            }
        }
    }

    public Audio(Assets assets, bool enabled)
    {
        this.assets = assets;
        Enabled = enabled;
        spatializer = new();
        if (enabled)
        {
            try
            {
                SoundEffect.Initialize();
                Console.WriteLine(AudioOutput.ConfigurePlainStereo());
            }
            catch (Exception ex) when (IsAudioFailure(ex))
            {
                Disable("Audio", ex);
            }
        }
    }

    public void Play(string group, long eventId, float volume = 1) => PlaySource(group, eventId, volume, null);

    public void PlayAt(string group, long eventId, float volume, Vector3 position) =>
        PlaySource(group, eventId, volume, position);

    public void PlayMenuAction()
    {
        if (!Enabled)
            return;
        menuSound?.Dispose();
        menuSound = null;
        if (Volume <= 0)
            return;
        try
        {
            var clips = assets.Data.Sounds["Footstep"];
            string path = clips[VariationKey(++menuSoundNumber) % (ulong)clips.Length];
            menuSound = assets.Sound(path).CreateInstance();
            menuSound.Volume = MenuVolume * Volume;
            menuSound.Play();
        }
        catch (Exception ex) when (IsAudioFailure(ex))
        {
            Disable("Menu audio", ex);
        }
    }

    private void PlaySource(string group, long eventId, float sourceVolume, Vector3? position)
    {
        if (!Enabled || played.Contains(eventId))
        {
            return;
        }

        played.Add(eventId);
        order.Enqueue(eventId);
        while (order.Count > 4096)
        {
            played.Remove(order.Dequeue());
        }

        if (Volume <= 0)
        {
            return;
        }

        if (assets.Data.Sounds.ContainsKey(mapId + "/" + group))
        {
            group = mapId + "/" + group;
        }

        if (!assets.Data.Sounds.TryGetValue(group, out var clips) || clips.Length == 0)
        {
            return;
        }

        try
        {
            Prune();
            ulong key = VariationKey(eventId);
            var info = assets.Data.SoundSettings.GetValueOrDefault(group) ?? new SoundSettingsData();
            float amount = 1 + info.PitchVariance;
            float ratio = MathHelper.Lerp(1 / amount, amount, (uint)(key >> 32) / (float)uint.MaxValue);
            string path = clips[key % (ulong)clips.Length];
            if (path.Length == 0)
            {
                return;
            }

            float baseVolume = Math.Clamp(sourceVolume * info.Volume, 0, 1);
            SoundEffect? owned = null;
            SoundEffect sound;
            if (position.HasValue)
            {
                var data = assets.SoundPcm(path);
                var pcm = AudioSpatializer.SpatialPcm(
                    data,
                    spatializer.Gains(position.Value, ListenerPosition) * baseVolume
                );
                owned = sound = new SoundEffect(pcm, data.SampleRate, AudioChannels.Stereo);
                baseVolume = 1;
            }
            else
            {
                sound = assets.Sound(path);
            }

            SoundEffectInstance? instance = null;
            try
            {
                instance = sound.CreateInstance();
                instance.Volume = baseVolume * Volume;
                instance.Pitch = Pitch(ratio);
                instance.Pan = 0;
                instance.Play();
                if (paused)
                {
                    instance.Pause();
                }

                shots.Add((instance, owned, baseVolume));
            }
            catch
            {
                instance?.Dispose();
                owned?.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (IsAudioFailure(ex))
        {
            Disable("Audio", ex);
        }
    }

    private static float Pitch(float ratio) => Math.Clamp(MathF.Log2(Math.Max(.5f, ratio)), -1, 1);

    public static ulong VariationKey(long eventId)
    {
        ulong value = unchecked((ulong)eventId + 0x9e3779b97f4a7c15UL);
        value = unchecked((value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94d049bb133111ebUL);
        return value ^ (value >> 31);
    }

    private static bool IsAudioFailure(Exception ex) =>
        ex is NoAudioHardwareException or InvalidOperationException or DllNotFoundException;

    private void Disable(string operation, Exception ex)
    {
        Console.Error.WriteLine(operation + " unavailable: " + ex.Message);
        Enabled = false;
        menuSound?.Dispose();
        menuSound = null;
        Reset();
    }

    private void Prune()
    {
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            if (shots[i].Instance.State == SoundState.Stopped)
            {
                shots[i].Instance.Dispose();
                shots[i].OwnedEffect?.Dispose();
                shots.RemoveAt(i);
            }
        }
    }

    public void Update()
    {
        if (Enabled)
        {
            Prune();
        }
    }

    public void SetPaused(bool value)
    {
        if (paused == value)
        {
            return;
        }

        paused = value;
        if (!Enabled)
        {
            return;
        }

        foreach (var sound in shots)
        {
            SetPause(sound.Instance, value);
        }

        foreach (var sound in ambient)
        {
            SetPause(sound.Instance, value);
        }

        foreach (var loop in flight)
        {
            if (loop != null)
            {
                if (value)
                {
                    loop.Pause();
                }
                else
                {
                    loop.Resume();
                }
            }
        }
    }

    private static void SetPause(SoundEffectInstance instance, bool value)
    {
        if (value && instance.State == SoundState.Playing)
        {
            instance.Pause();
        }
        else if (!value && instance.State == SoundState.Paused)
        {
            instance.Resume();
        }
    }

    public void PlayEvents(IEnumerable<SimulationEvent> events)
    {
        foreach (var e in events)
        {
            long id = e.Id << 3;
            var position = new Vector3(e.X.ToFloat(), e.Y.ToFloat(), 0);
            if (e.Kind == SimulationEventKind.Hit)
            {
                if (e.HitKind == HitKind.Bat)
                {
                    PlayAt("BatHit" + Math.Clamp(e.ComboHits, 1, 5), id, .5f, position);
                    PlayAt("BatHitVoice" + Math.Clamp(e.ComboHits, 1, 5), id | 1, .5f, position);
                }
                else if (e.HitKind == HitKind.Bouncer)
                {
                    PlayAt("CharacterCollision", id, .5f, position);
                }
                else
                {
                    PlayAt("TongueCollide", id, .5f, position);
                }

                continue;
            }

            if (e.Kind == SimulationEventKind.Launch)
            {
                PlayAt("Launch3", id, .5f, position);
                PlayAt("LaunchVoice3", id | 1, .5f, position);
                continue;
            }

            if (e.Kind == SimulationEventKind.RoundWin)
            {
                Play("VictorySting", id, .5f);
                continue;
            }

            if (e.Kind == SimulationEventKind.Swing && e.Power > Fixed.FromDecimal(.25m))
            {
                PlayAt("BatSwingVoice", id | 1, .4f, position);
            }

            if (e.Kind == SimulationEventKind.Bounce)
            {
                PlayAt("FrogBounceVoice", id | 1, .4f, position);
            }

            string? group = e.Kind switch
            {
                SimulationEventKind.Spawn => "CharacterSpawn",
                SimulationEventKind.Jump => "Jump",
                SimulationEventKind.Land => "Land",
                SimulationEventKind.Footstep => "Footstep",
                SimulationEventKind.Charge => "BatChargeup",
                SimulationEventKind.Swing => "BatSwing",
                SimulationEventKind.TongueLaunch => "TongueLaunch",
                SimulationEventKind.TongueLatch => "TongueCollideSurface",
                SimulationEventKind.Burp => "Burp",
                SimulationEventKind.Bounce => "FrogBounce",
                SimulationEventKind.Death => e.Other < 0 ? "KnockoutSuicide"
                : e.Strength <= 1 ? "Knockout1"
                : e.Strength <= 3 ? "Knockout2"
                : "Knockout3",
                _ => null,
            };
            float callVolume = e.Kind switch
            {
                SimulationEventKind.Swing => .4f + e.Strength.ToFloat() * .4f,
                SimulationEventKind.Spawn or SimulationEventKind.Jump or SimulationEventKind.Land => .4f,
                SimulationEventKind.Footstep => .1f,
                SimulationEventKind.Death => e.Other < 0 ? .45f
                : e.Strength <= 1 ? .55f
                : e.Strength <= 3 ? .65f
                : .75f,
                _ => .5f,
            };
            if (group != null)
            {
                PlayAt(group, id, callVolume, position);
            }
        }
    }

    public void Ambient(MapData map)
    {
        mapId = map.Id;
        foreach (var sound in ambient)
        {
            sound.Instance.Dispose();
        }

        ambient.Clear();
        if (!Enabled)
        {
            return;
        }

        foreach (var source in map.Audio.Where(s => s.Active && s.PlayOnAwake))
        {
            try
            {
                var instance = assets.Sound(source.Path).CreateInstance();
                instance.IsLooped = source.Loop;
                instance.Volume = Math.Clamp(Volume * source.Volume, 0, 1);
                instance.Pitch = Pitch(source.Pitch);
                instance.Pan = source.Pan;
                instance.Play();
                if (paused)
                {
                    instance.Pause();
                }

                ambient.Add((instance, source.Volume));
            }
            catch (Exception ex) when (IsAudioFailure(ex))
            {
                Disable("Ambient audio", ex);
                break;
            }
        }
    }

    public void UpdateFlights(World world, float dt)
    {
        if (!Enabled || paused)
        {
            return;
        }

        Prune();
        if (world.Phase is MatchPhase.RoundScores or MatchPhase.MatchFinished)
        {
            foreach (var sound in shots)
            {
                sound.Instance.Dispose();
                sound.OwnedEffect?.Dispose();
            }

            shots.Clear();
            foreach (var sound in ambient)
            {
                sound.Instance.Dispose();
            }

            ambient.Clear();
            for (int i = 0; i < flight.Length; i++)
            {
                StopFlight(i, true);
            }

            mapId = "";
            return;
        }

        if (mapId != world.Map.Id)
        {
            Ambient(world.Map);
        }

        if (!assets.Data.Sounds.TryGetValue("flight", out var clips))
        {
            return;
        }

        float heightMod = assets.Data.CharacterVisualParameters.GetValueOrDefault("flightHeightPitchMod", .055555556f);
        float speedMod = assets.Data.CharacterVisualParameters.GetValueOrDefault("flightVelocityVolumeMod", .001f);
        bool modVolume = assets.Data.CharacterVisualParameters.GetValueOrDefault("modFlightVolume", 1) != 0;
        try
        {
            foreach (var p in world.Players)
            {
                int i = p.Slot;
                if (!p.Alive)
                {
                    StopFlight(i, true);
                    continue;
                }

                if (p.Mode != CharacterMode.Bouncing)
                {
                    flightVolume[i] = Math.Max(0, flightVolume[i] - dt * 2);
                    if (flight[i] != null)
                    {
                        flight[i]!.Volume = Volume * flightVolume[i];
                    }

                    continue;
                }

                if (p.HitstopTicks > 0 && p.HitstopScale == Fixed.Zero)
                {
                    StopFlight(i, false);
                    flightOrigin[i] = p.Y.ToFloat();
                    continue;
                }

                if (p.HasBounceDodged)
                {
                    StopFlight(i, false);
                    continue;
                }

                flightVolume[i] = Math.Clamp(modVolume ? p.Velocity.Length.ToFloat() * speedMod : .15f, 0, 1);
                float pitch = 1 + (p.Y.ToFloat() - flightOrigin[i]) * heightMod;
                if (flight[i] == null || flight[i]!.State != SoundState.Playing)
                {
                    int level =
                        p.HitsTaken > 4 ? 2
                        : p.HitsTaken > 2 ? 1
                        : p.HitsTaken > 0 ? 0
                        : flightLevel[i];
                    if (level < 0 || level >= clips.Length || clips[level].Length == 0)
                    {
                        flight[i]?.Dispose();
                        flight[i] = null;
                        flightLevel[i] = level;
                        continue;
                    }

                    if (flight[i] == null || level != flightLevel[i])
                    {
                        flight[i]?.Dispose();
                        flight[i] = new DynamicFlightLoop(assets.SoundPcm(clips[level]));
                        flightLevel[i] = level;
                    }

                    flight[i]!.Volume = Volume * flightVolume[i];
                    flight[i]!.Rate = pitch;
                    flight[i]!.Play();
                }

                flight[i]!.Volume = Volume * flightVolume[i];
                flight[i]!.Rate = pitch;
            }
        }
        catch (Exception ex) when (IsAudioFailure(ex))
        {
            Disable("Flight audio", ex);
        }
    }

    private void StopFlight(int i, bool dispose)
    {
        flight[i]?.Stop();
        flightVolume[i] = 0;
        if (dispose)
        {
            flight[i]?.Dispose();
            flight[i] = null;
            flightLevel[i] = -1;
            flightOrigin[i] = 0;
        }
    }

    public void Reset()
    {
        played.Clear();
        order.Clear();
        paused = false;
        foreach (var sound in shots)
        {
            sound.Instance.Dispose();
            sound.OwnedEffect?.Dispose();
        }

        shots.Clear();
        foreach (var sound in ambient)
        {
            sound.Instance.Dispose();
        }

        ambient.Clear();
        for (int i = 0; i < flight.Length; i++)
        {
            StopFlight(i, true);
        }

        mapId = "";
    }

    public void Dispose()
    {
        Reset();
        menuSound?.Dispose();
        menuSound = null;
    }
}
