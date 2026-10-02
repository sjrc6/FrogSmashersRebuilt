namespace FrogSmashers.Network;

internal sealed partial class MeshLobby
{
    private enum LobbyPhase
    {
        AwaitingSimulation,
        Lobby,
        Updating,
        Match,
    }

    private enum CheckpointPhase
    {
        Pausing,
        Confirming,
        Connecting,
        Loading,
        AwaitingCommit,
        Committed,
    }

    public enum ControlKind
    {
        Hello,
        State,
        Heartbeat,
        Mesh,
        MeshAcknowledged,
        Leave,
        Error,
        Pause,
        Paused,
        Confirm,
        Confirmed,
        InputPrefix,
        Checkpoint,
        Chunk,
        Loaded,
        Commit,
        Cancel,
    }
}
