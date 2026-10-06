using FrogSmashers.Client;

internal static class ToastTests
{
    public static void Run(Action<bool, string> check)
    {
        var toasts = new ToastController();
        toasts.Show("FIRST");
        check(toasts.CurrentVisibility == 0 && toasts.Previous == null, "toast enters from offscreen");
        toasts.Update(ToastController.TransitionSeconds / 2);
        check(
            toasts.CurrentVisibility > .5f && toasts.CurrentVisibility < .9f,
            "toast decelerates with exponential out easing"
        );
        toasts.Update(ToastController.TransitionSeconds / 2);
        toasts.Show("SECOND");
        check(
            toasts.Previous == "FIRST" && toasts.PreviousVisibility == 1,
            "replacement keeps previous toast for exit"
        );
        toasts.Update(ToastController.TransitionSeconds / 10);
        check(
            toasts.PreviousVisibility - toasts.CurrentVisibility > .2f,
            "equal-height outgoing toast remains visibly exposed below incoming toast early in the transition"
        );
        check(
            Math.Abs(toasts.CurrentVisibility + toasts.PreviousVisibility - 1) < .0001f,
            "incoming and outgoing use the same transition progress"
        );
        toasts.Show("THIRD");
        check(toasts.Previous == "SECOND" && toasts.Current == "THIRD", "burst replaces latest toast without a queue");
        toasts.Update(ToastController.TransitionSeconds);
        check(toasts.Previous == null && toasts.CurrentVisibility == 1, "only one toast remains after replacement");
        toasts.Update(3);
        toasts.Show("THIRD");
        check(
            toasts.CurrentVisibility == 0 && toasts.Previous == "THIRD" && toasts.PreviousVisibility == 1,
            "repeated message replaces itself with a fresh entrance"
        );
        toasts.Show("THIRD");
        check(toasts.PreviousVisibility == 1, "same-frame retrigger preserves the outgoing visible toast");
        toasts.Update(ToastController.TransitionSeconds / 10);
        check(
            toasts.CurrentVisibility > 0 && toasts.PreviousVisibility > toasts.CurrentVisibility,
            "repeated toast enters while its previous copy retracts"
        );
        toasts.Update(ToastController.DisplaySeconds - ToastController.TransitionSeconds / 10 - .1);
        check(toasts.Current == "THIRD", "repeated toast gets a new display timer");
        toasts.Update(.2);
        check(toasts.Current == null && toasts.Previous == "THIRD", "expired message retracts");
        toasts.Update(ToastController.TransitionSeconds);
        check(toasts.Previous == null, "expired toast clears completely");
        check(
            UserMessages.ConnectionError("Steam runtime unavailable: technical detail") == "STEAM UNAVAILABLE",
            "Steam details use concise user copy"
        );
        check(
            UserMessages.ConnectionError("SIMULATION BUILD MISMATCH") == "DIFFERENT GAME VERSIONS",
            "build mismatch has one user message"
        );
        check(
            UserMessages.ConnectionError("arbitrary remote detail") == "CONNECTION LOST",
            "unknown connection details are not displayed verbatim"
        );
        check(
            UserMessages.ConnectionError("LOBBY CHECKPOINT MISMATCH") == "MATCH DESYNC",
            "Runtime checkpoint mismatches do not masquerade as different game versions"
        );
        check(
            UserMessages.ConnectionError("REMOVED BY HOST") == "REMOVED FROM LOBBY",
            "The current kick rejection maps to its player-facing message"
        );
        check(
            UserMessages.ConnectionError("LOBBY UNAVAILABLE") == "LOBBY UNAVAILABLE",
            "The current admission rejection preserves its specific explanation"
        );
        check(
            UserMessages.MatchStart(FrogSmashers.Core.MatchStartBlock.TooFewPlayers) == null
                && UserMessages.MatchStart(FrogSmashers.Core.MatchStartBlock.TooFewTeams) == null,
            "Obvious start requirements disable the button silently"
        );
    }
}
