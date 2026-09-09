// Minimal stand-ins so StanceState can be exercised outside the game.
//
// StanceState has two halves: ReadAutoState, which reads the game, and Update,
// which is pure decision logic. Only the second is worth testing, but the file
// has to compile, so the game-reading surface is stubbed to inert defaults.
// The tests set the Suspend* fields directly and never call ReadAutoState.
namespace SPTFreeAim.Compat
{
    public class BoolProbe
    {
        public BoolProbe(string label, params string[] candidates) { }
        public bool Read(object target) { return false; }
        public bool Resolved { get { return false; } }
        public string Describe() { return ""; }
    }

    public static class GameRefs
    {
        public static readonly BoolProbe Probe_SprintOnContext = new BoolProbe("");
        public static readonly BoolProbe Probe_SprintOnPlayer = new BoolProbe("");
        public static readonly BoolProbe Probe_InventoryOpen = new BoolProbe("");
        public static readonly BoolProbe Probe_Reloading = new BoolProbe("");

        public static object GetMovementContext(object player) { return null; }
        public static object GetHandsController(object player) { return null; }
        public static string GetMovementStateName(object mc) { return ""; }
        public static bool HoldingNonFirearm(object player) { return false; }
    }
}

namespace BepInEx.Configuration
{
    public struct KeyboardShortcut
    {
        public bool IsDown() { return false; }
        public bool IsPressed() { return false; }
    }
}
