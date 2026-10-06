namespace FrogSmashers.Core;

public enum MatchPhase
{
    Playing,
    RoundFinished,
    MatchFinished,
    RoundScores,
    Selecting,
    ChoosingCrews,
}

public enum Participation
{
    Active,
    Waiting,
    Eliminated,
    Inactive,
}

public sealed class PlayerProgress
{
    public int Score;
    public int RoundWins;
    public int TotalScore;
    public int Stocks;
    public Participation Participation;
    public bool Ready;

    internal void WriteSnapshot(BinaryWriter writer)
    {
        writer.Write(Score);
        writer.Write(RoundWins);
        writer.Write(TotalScore);
        writer.Write(Stocks);
        writer.Write((int)Participation);
        writer.Write(Ready);
    }

    internal static PlayerProgress ReadSnapshot(BinaryReader reader) =>
        new()
        {
            Score = reader.ReadInt32(),
            RoundWins = reader.ReadInt32(),
            TotalScore = reader.ReadInt32(),
            Stocks = reader.ReadInt32(),
            Participation = (Participation)reader.ReadInt32(),
            Ready = reader.ReadBoolean(),
        };
}

public sealed class MatchState
{
    public int CurrentMapIndex;
    public int RoundNumber = 1;
    public MatchPhase Phase;
    public int Winner = -1;
    public int PhaseTicks;
    public bool IsShowdown;
    public PlayerProgress[] Players { get; }
    public int[] CrewTeams { get; } = Enumerable.Repeat(-1, 8).ToArray();

    public int Team(GameRules rules, int slot) =>
        rules.Format == MatchFormat.Crews ? CrewTeams[slot] : rules.Teams[slot];

    public int[] TeamSelections { get; } = Enumerable.Repeat(-1, 8).ToArray();

    public MatchState(GameRules rules)
    {
        IsShowdown = rules.Showdown && rules.Format != MatchFormat.Crews;
        Players = Enumerable
            .Range(0, rules.PlayerCount)
            .Select(_ => new PlayerProgress
            {
                Stocks = rules.StartingStocks,
                Participation = rules.Lobby ? Participation.Inactive : Participation.Active,
            })
            .ToArray();
    }

    public bool IsWinner(GameRules rules, int slot) =>
        Winner >= 0 && (rules.UsesTeams ? Team(rules, slot) == Team(rules, Winner) : slot == Winner);

    public void StartRound(GameRules rules)
    {
        Phase = MatchPhase.Playing;
        Winner = -1;
        PhaseTicks = 0;
        foreach (var player in Players)
        {
            player.Score = 0;
            player.Ready = false;
            if (!rules.Lobby && !IsShowdown)
            {
                player.Stocks = rules.StartingStocks;
                player.Participation = rules.Format == MatchFormat.Crews ? Participation.Waiting : Participation.Active;
            }
            else if (IsShowdown && rules.Scoring == ScoringMode.Stocks)
                player.Stocks = 1;
        }
        Array.Fill(TeamSelections, -1);
        if (rules.Format == MatchFormat.Crews)
        {
            IsShowdown = false;
            Array.Fill(CrewTeams, -1);
            Phase = MatchPhase.ChoosingCrews;
        }
    }

    public int RecordDeath(GameRules rules, int slot, int attacker, int hits)
    {
        if (rules.Lobby || Phase != MatchPhase.Playing || Players[slot].Participation != Participation.Active)
            return -1;
        if (rules.Scoring == ScoringMode.Stocks)
        {
            var victim = Players[slot];
            victim.Stocks = Math.Max(0, victim.Stocks - Math.Max(1, hits));
            if (victim.Stocks > 0)
                return -1;
            victim.Participation = Participation.Eliminated;
            return rules.Format == MatchFormat.Crews
                ? Array.FindIndex(Players, player => player.Participation == Participation.Active)
                : WinningSide(rules, RemainingPlayers());
        }
        if (IsShowdown)
        {
            Players[slot].Participation = Participation.Eliminated;
            var survivors = Enumerable
                .Range(0, Players.Length)
                .Where(index => Players[index].Participation == Participation.Active)
                .ToArray();
            return WinningSide(rules, survivors);
        }
        int points = DeathScore(rules, attacker, hits);
        int credited = points < 0 ? slot : attacker;
        if (credited < 0 || credited >= Players.Length || points == 0)
            return -1;
        for (int index = 0; index < Players.Length; index++)
            if (index == credited || rules.UsesTeams && Team(rules, index) == Team(rules, credited))
                Players[index].Score += points;
        return Players[credited].Score >= rules.TargetScore ? credited : -1;
    }

    public int DeathScore(GameRules rules, int attacker, int hits)
    {
        if (rules.Lobby || Phase != MatchPhase.Playing)
            return 0;
        if (rules.Scoring == ScoringMode.Stocks)
            return -Math.Max(1, hits);
        if (IsShowdown)
            return 0;
        if (attacker < 0 || attacker >= Players.Length)
            return rules.Modifiers.SuicidePenalty ? -1 : 0;
        return Math.Max(1, hits);
    }

    public int RoundContribution(GameRules rules, int slot) =>
        rules.CumulativeScoring && !IsShowdown ? Players[slot].Score
        : IsWinner(rules, slot) ? 1
        : 0;

    public void WinRound(GameRules rules, int slot)
    {
        Phase = MatchPhase.RoundFinished;
        Winner = slot;
        PhaseTicks = rules.RoundFinishTicks;
        if (HasMoreCrewBouts(rules))
            return;
        for (int index = 0; index < Players.Length; index++)
        {
            if (IsWinner(rules, index))
                Players[index].RoundWins++;
            Players[index].TotalScore += RoundContribution(rules, index);
        }
    }

    private bool HasMoreCrewBouts(GameRules rules) =>
        rules.Format == MatchFormat.Crews && WinningSide(rules, RemainingPlayers()) < 0;

    public void EndCelebration(GameRules rules)
    {
        if (HasMoreCrewBouts(rules))
            BeginCrewSelection(rules);
        else
        {
            Phase = MatchPhase.RoundScores;
            PhaseTicks = rules.ScoreScreenTicks;
        }
    }

    public bool AdvanceRound(GameRules rules)
    {
        if (IsShowdown || rules.Format == MatchFormat.Crews)
        {
            Phase = MatchPhase.MatchFinished;
            return false;
        }
        if (RoundNumber >= rules.MatchRounds)
        {
            int highest = Players.Max(player => player.TotalScore);
            int[] leaders = Enumerable
                .Range(0, Players.Length)
                .Where(index => Players[index].TotalScore == highest)
                .ToArray();
            int winner = WinningSide(rules, leaders);
            if (winner >= 0)
            {
                Winner = winner;
                Phase = MatchPhase.MatchFinished;
                return false;
            }
            IsShowdown = true;
            foreach (var player in Players)
                player.Participation = player.TotalScore == highest ? Participation.Active : Participation.Eliminated;
        }
        else
            RoundNumber++;
        return true;
    }

    private int WinningSide(GameRules rules, int[] candidates)
    {
        if (candidates.Length == 0)
            return -1;
        int first = candidates[0];
        if (candidates.Length == 1)
            return first;
        if (!rules.UsesTeams)
            return -1;
        foreach (int slot in candidates)
            if (Team(rules, slot) != Team(rules, first))
                return -1;
        return first;
    }

    public bool CanBackOut(GameRules rules, int slot) =>
        Phase == MatchPhase.Selecting
        && TeamSelections[Team(rules, slot)] == slot
        && Players[slot].Participation == Participation.Waiting;

    public void ApplyCommand(GameRules rules, int source, MatchCommand command)
    {
        if (source < 0 || source >= Players.Length || rules.CpuPlayers[source])
            return;
        if (command.Kind == MatchCommandKind.SkipCelebration)
        {
            if (Phase == MatchPhase.RoundFinished && Winner == source)
                PhaseTicks = 0;
            return;
        }
        if (Phase == MatchPhase.ChoosingCrews)
        {
            ApplyCrewChoice(source, command);
            return;
        }
        if (Phase != MatchPhase.Selecting || rules.Format != MatchFormat.Crews)
            return;
        int team = Team(rules, source);
        var player = Players[source];
        switch (command.Kind)
        {
            case MatchCommandKind.SelectFighter:
                if (
                    command.Selection == source
                    && TeamSelections[team] < 0
                    && player.Participation == Participation.Waiting
                )
                {
                    TeamSelections[team] = source;
                    player.Ready = false;
                }
                break;
            case MatchCommandKind.BackOutFighter:
                if (CanBackOut(rules, source))
                {
                    TeamSelections[team] = -1;
                    player.Ready = false;
                }
                break;
            case MatchCommandKind.ToggleReady:
                if (TeamSelections[team] == source)
                    player.Ready = !player.Ready;
                break;
        }
    }

    private void ApplyCrewChoice(int slot, MatchCommand command)
    {
        var player = Players[slot];
        if (
            command.Kind == MatchCommandKind.ChooseCrew
            && command.Selection < 2
            && CrewTeams[slot] != command.Selection
        )
        {
            CrewTeams[slot] = command.Selection;
            player.Ready = false;
        }
        else if (command.Kind == MatchCommandKind.ToggleReady && CrewTeams[slot] >= 0)
            player.Ready = !player.Ready;
    }

    public void CompleteCrewChoices()
    {
        if (Phase != MatchPhase.ChoosingCrews || Players.Any(player => !player.Ready))
            return;
        var teams = CrewTeams.Take(Players.Length).ToArray();
        if (teams.Any(team => team < 0) || !teams.Contains(0) || !teams.Contains(1))
            return;
        foreach (var player in Players)
            player.Ready = false;
        Phase = MatchPhase.Selecting;
    }

    private int[] RemainingPlayers() =>
        Enumerable
            .Range(0, Players.Length)
            .Where(slot => Players[slot].Participation is Participation.Active or Participation.Waiting)
            .ToArray();

    private void BeginCrewSelection(GameRules rules)
    {
        RoundNumber++;
        Phase = MatchPhase.Selecting;
        Winner = -1;
        PhaseTicks = 0;
        Array.Fill(TeamSelections, -1);
        foreach (var player in Players)
            player.Ready = false;
        foreach (int slot in RemainingPlayers())
            if (Players[slot].Participation == Participation.Active)
                TeamSelections[Team(rules, slot)] = slot;
    }

    public bool CompleteCrewSelection(GameRules rules)
    {
        if (rules.Format != MatchFormat.Crews || Phase != MatchPhase.Selecting)
            return false;
        int[] remaining = RemainingPlayers();
        if (
            remaining
                .Select(slot => Team(rules, slot))
                .Distinct()
                .Any(team => TeamSelections[team] < 0 || !Players[TeamSelections[team]].Ready)
        )
            return false;
        foreach (int slot in remaining)
            Players[slot].Participation =
                TeamSelections[Team(rules, slot)] == slot ? Participation.Active : Participation.Waiting;
        IsShowdown = remaining.Length == 2;
        Phase = MatchPhase.Playing;
        return true;
    }

    internal void WriteSnapshot(BinaryWriter writer)
    {
        writer.Write(CurrentMapIndex);
        writer.Write(RoundNumber);
        writer.Write((int)Phase);
        writer.Write(Winner);
        writer.Write(PhaseTicks);
        writer.Write(IsShowdown);
        foreach (var player in Players)
            player.WriteSnapshot(writer);
        foreach (int team in CrewTeams)
            writer.Write(team);
        foreach (int selection in TeamSelections)
            writer.Write(selection);
    }

    internal static MatchState ReadSnapshot(BinaryReader reader, GameRules rules, int mapCount)
    {
        var state = new MatchState(rules)
        {
            CurrentMapIndex = reader.ReadInt32(),
            RoundNumber = reader.ReadInt32(),
            Phase = (MatchPhase)reader.ReadInt32(),
            Winner = reader.ReadInt32(),
            PhaseTicks = reader.ReadInt32(),
            IsShowdown = reader.ReadBoolean(),
        };
        if (
            state.CurrentMapIndex < 0
            || state.CurrentMapIndex >= mapCount
            || state.RoundNumber < 1
            || !Enum.IsDefined(state.Phase)
            || state.Winner < -1
            || state.Winner >= rules.PlayerCount
        )
            throw new InvalidDataException("Invalid match snapshot");
        for (int slot = 0; slot < state.Players.Length; slot++)
        {
            var player = PlayerProgress.ReadSnapshot(reader);
            if (player.RoundWins < 0 || player.Stocks < 0 || !Enum.IsDefined(player.Participation))
                throw new InvalidDataException("Invalid progression snapshot");
            state.Players[slot] = player;
        }
        for (int slot = 0; slot < state.CrewTeams.Length; slot++)
        {
            int team = reader.ReadInt32();
            bool assigned =
                rules.Format == MatchFormat.Crews
                && slot < rules.PlayerCount
                && state.Phase != MatchPhase.ChoosingCrews;
            if (
                team < (assigned ? 0 : -1)
                || team > 1
                || (rules.Format != MatchFormat.Crews || slot >= rules.PlayerCount) && team != -1
            )
                throw new InvalidDataException("Invalid crew assignment snapshot");
            state.CrewTeams[slot] = team;
        }
        for (int team = 0; team < state.TeamSelections.Length; team++)
        {
            int selection = reader.ReadInt32();
            if (
                selection < -1
                || selection >= rules.PlayerCount
                || selection >= 0 && state.Team(rules, selection) != team
            )
                throw new InvalidDataException("Invalid team selection snapshot");
            state.TeamSelections[team] = selection;
        }
        return state;
    }
}
