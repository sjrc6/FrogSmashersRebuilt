using FrogSmashers.Core;

namespace FrogSmashers.Client;

internal static class UserMessages
{
    public static string? MatchStart(MatchStartBlock reason) =>
        reason == MatchStartBlock.CrewTeamCount ? "CREWS REQUIRES TWO TEAMS" : null;

    public static string ConnectionError(string detail)
    {
        string error = detail.ToUpperInvariant();
        if (
            error.Contains("DESYNC")
            || error.Contains("CHECKPOINT MISMATCH")
            || error.Contains("ROLLBACK HISTORY")
            || error.Contains("ALREADY SUBMITTED INPUT")
            || error.Contains("AGREED MATCH")
        )
            return "MATCH DESYNC";
        if (
            error.Contains("MISMATCH") && !error.Contains("RUNTIME")
            || error.Contains("DOES NOT MATCH")
            || error.Contains("ANOTHER GAME")
        )
            return "DIFFERENT GAME VERSIONS";
        if (error.Contains("TIMED OUT") || error.Contains("STOPPED ACKNOWLEDGING"))
            return "CONNECTION TIMED OUT";
        if (error.Contains("BANNED"))
            return "BANNED FROM LOBBY";
        if (error == "REMOVED BY HOST")
            return "REMOVED FROM LOBBY";
        if (error.Contains("NOT ENOUGH OPEN SLOTS"))
            return "NOT ENOUGH SLOTS";
        if (error.Contains("ALREADY STARTED") || error == "LOBBY UNAVAILABLE")
            return "LOBBY UNAVAILABLE";
        if (error.Contains("STEAM RUNTIME") || error.Contains("STEAM INITIALIZATION") || error.Contains("APPID"))
            return "STEAM UNAVAILABLE";
        if (error.Contains("CREATION FAILED") || error.Contains("LISTEN SOCKET"))
            return "CANNOT HOST LOBBY";
        if (error.Contains("ENTRY FAILED") || error.Contains("CONNECTION COULD NOT"))
            return "CANNOT JOIN LOBBY";
        return "CONNECTION LOST";
    }
}
