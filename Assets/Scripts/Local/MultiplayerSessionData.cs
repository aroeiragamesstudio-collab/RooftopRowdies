using UnityEngine;
using UnityEngine.InputSystem;

public class MultiplayerSessionData : MonoBehaviour
{
    public static MultiplayerSessionData Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ─────────────────────────────────────────────
    // Data
    // ─────────────────────────────────────────────

    public enum CharacterType { None, Gun, Jump }

    /// <summary>
    /// Holds all data for one player slot after the lobby finishes.
    /// </summary>
    [System.Serializable]
    public class PlayerAssignment
    {
        /// <summary>The physical device this player joined with (Keyboard or Gamepad).</summary>
        public InputDevice device;

        /// <summary>The control scheme name: "WASD", "Arrows", or "Gamepad".</summary>
        public string controlScheme;

        /// <summary>Which character this player selected in the lobby.</summary>
        public CharacterType character = CharacterType.None;

        /// <summary>Whether this slot has been confirmed by the player.</summary>
        public bool confirmed = false;
    }

    /// <summary>Slot 0 = first player to join, Slot 1 = second.</summary>
    public PlayerAssignment[] players = new PlayerAssignment[2]
    {
        new PlayerAssignment(),
        new PlayerAssignment()
    };

    /// <summary>True once both players have confirmed in the lobby.</summary>
    public bool IsReady =>
        players[0].confirmed &&
        players[1].confirmed &&
        players[0].character != CharacterType.None &&
        players[1].character != CharacterType.None &&
        players[0].character != players[1].character; // must be different characters

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    /// <summary>
    /// Resets all assignment data. Call this when returning to the menu
    /// so the next lobby session starts clean.
    /// </summary>
    public void Reset()
    {
        players[0] = new PlayerAssignment();
        players[1] = new PlayerAssignment();
    }

    /// <summary>
    /// Returns the assignment for the player who chose the given character type.
    /// Returns null if no player chose that character.
    /// </summary>
    public PlayerAssignment GetAssignmentFor(CharacterType type)
    {
        foreach (var p in players)
        {
            if (p.character == type) return p;
        }
        return null;
    }
}
