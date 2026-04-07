using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;
using UnityEngine.SceneManagement;

public class LocalMultiplayerManager : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // Inspector Fields
    // ─────────────────────────────────────────────

    [Header("Characters (pre-placed in scene)")]
    [Tooltip("The GameObject that has GunCharacterController and PlayerInput.")]
    public GameObject gunCharacter;

    [Tooltip("The GameObject that has JumpCharacterController and PlayerInput.")]
    public GameObject jumpCharacter;

    [Header("Mode")]
    [Tooltip("True = Player 1 always gets Gun, Player 2 always gets Jump.")]
    public bool PROTOTYPE_MODE = true;

    // ─────────────────────────────────────────────
    // Internal Types
    // ─────────────────────────────────────────────

    private class JoinedPlayer
    {
        public int slotIndex;      // 0 = first to press, 1 = second
        public InputDevice device;        // Physical device that joined
        public string controlScheme;  // "WASD", "Arrows", or "Gamepad"
    }

    // ─────────────────────────────────────────────
    // Internal State
    // ─────────────────────────────────────────────

    private JoinedPlayer[] joined = new JoinedPlayer[2];
    private int joinedCount = 0;
    private bool assignmentDone = false;

    // Tracks registered devices to prevent double-joining.
    private HashSet<int> registeredGamepadIds = new HashSet<int>();
    private HashSet<string> registeredKeyboardSchemes = new HashSet<string>();

    // ─────────────────────────────────────────────
    // Unity Lifecycle
    // ─────────────────────────────────────────────

    private void Awake()
    {
        if (gunCharacter == null)
            Debug.LogError("[MultiplayerManager] gunCharacter is not assigned in the Inspector!");
        if (jumpCharacter == null)
            Debug.LogError("[MultiplayerManager] jumpCharacter is not assigned in the Inspector!");
    }

    private void Start()
    {
        // Wire cross-references immediately since both characters are already
        // in the scene. Input assignment happens separately when players join.
        WireCrossReferences();
        EnableJoining();
    }

    private void OnDestroy()
    {
        DisableJoining();
    }

    // ─────────────────────────────────────────────
    // Cross-Reference Wiring
    // ─────────────────────────────────────────────

    /// <summary>
    /// Directly assigns each controller's reference to the other character,
    /// and connects both DistanceJoint2D components.
    /// This runs once at Start() since both objects are already in the scene.
    /// </summary>
    private void WireCrossReferences()
    {
        GunCharacterController gunCtrl = gunCharacter.GetComponent<GunCharacterController>();
        JumpCharacterController jumpCtrl = jumpCharacter.GetComponent<JumpCharacterController>();
        GunController gunWeapon = gunCharacter.GetComponentInChildren<GunController>();

        Rigidbody2D gunRb = gunCharacter.GetComponent<Rigidbody2D>();
        Rigidbody2D jumpRb = jumpCharacter.GetComponent<Rigidbody2D>();
        DistanceJoint2D gunJoint = gunCharacter.GetComponent<DistanceJoint2D>();
        DistanceJoint2D jumpJoint = jumpCharacter.GetComponent<DistanceJoint2D>();

        if (gunCtrl != null)
            gunCtrl.paws = jumpCtrl;
        else
            Debug.LogError("[MultiplayerManager] GunCharacterController not found on gunCharacter.");

        if (gunWeapon != null)
            gunWeapon.jumpChar = jumpCtrl;
        else
            Debug.LogError("[MultiplayerManager] GunController not found in gunCharacter children.");

        if (jumpCtrl != null)
            jumpCtrl.porky = gunCtrl;
        else
            Debug.LogError("[MultiplayerManager] JumpCharacterController not found on jumpCharacter.");

        if (gunJoint != null && jumpRb != null)
            gunJoint.connectedBody = jumpRb;
        else
            Debug.LogError("[MultiplayerManager] Could not connect gun DistanceJoint2D.");

        if (jumpJoint != null && gunRb != null)
            jumpJoint.connectedBody = gunRb;
        else
            Debug.LogError("[MultiplayerManager] Could not connect jump DistanceJoint2D.");

        Debug.Log("[MultiplayerManager] Cross-references wired.");
    }

    // ─────────────────────────────────────────────
    // Joining
    // ─────────────────────────────────────────────

    private void EnableJoining()
    {
        // Each PlayerInput component in the scene with "Invoke Unity Events"
        // behavior also calls listenForUnpairedDeviceActivity internally.
        // To avoid conflicting with that counter, we do NOT touch
        // listenForUnpairedDeviceActivity here at all.
        // Instead we poll raw device input directly in Update(),
        // which is simpler and doesn't interfere with anything.
        Debug.Log("[MultiplayerManager] Waiting for players to press a button...");
    }

    private void DisableJoining()
    {
        // Nothing to unsubscribe since we use Update() polling instead of
        // InputUser.onUnpairedDeviceUsed.
    }

    // ─────────────────────────────────────────────
    // Update — Press to Join Detection
    // ─────────────────────────────────────────────

    private void Update()
    {
        if (assignmentDone) return;
        if (joinedCount >= 2) return;

        // ── Check all connected gamepads ──────────────────────────────────────
        foreach (Gamepad gp in Gamepad.all)
        {
            if (registeredGamepadIds.Contains(gp.deviceId)) continue;

            // Any button press on this gamepad counts as joining.
            if (gp.wasUpdatedThisFrame && AnyButtonPressed(gp))
            {
                RegisterPlayer(gp, "Gamepad");
                if (joinedCount >= 2) break;
            }
        }

        if (joinedCount >= 2)
        {
            OnBothPlayersJoined();
            return;
        }

        // ── Check keyboard ────────────────────────────────────────────────────
        if (Keyboard.current != null && Keyboard.current.wasUpdatedThisFrame)
        {
            // WASD join key: W
            if (!registeredKeyboardSchemes.Contains("WASD") &&
                Keyboard.current.wKey.wasPressedThisFrame)
            {
                RegisterPlayer(Keyboard.current, "WASD");
            }

            // Arrows join key: Up Arrow
            if (!registeredKeyboardSchemes.Contains("Arrows") &&
                Keyboard.current.upArrowKey.wasPressedThisFrame)
            {
                RegisterPlayer(Keyboard.current, "Arrows");
            }
        }

        if (joinedCount >= 2)
            OnBothPlayersJoined();
    }

    /// <summary>
    /// Returns true if any button on the gamepad was pressed this frame.
    /// We check the most common face/shoulder/stick buttons to detect a join press.
    /// </summary>
    private bool AnyButtonPressed(Gamepad gp)
    {
        return gp.buttonSouth.wasPressedThisFrame ||
               gp.buttonNorth.wasPressedThisFrame ||
               gp.buttonEast.wasPressedThisFrame ||
               gp.buttonWest.wasPressedThisFrame ||
               gp.startButton.wasPressedThisFrame ||
               gp.selectButton.wasPressedThisFrame ||
               gp.leftShoulder.wasPressedThisFrame ||
               gp.rightShoulder.wasPressedThisFrame;
    }

    private void RegisterPlayer(InputDevice device, string scheme)
    {
        int slotIndex = joinedCount;
        joined[slotIndex] = new JoinedPlayer
        {
            slotIndex = slotIndex,
            device = device,
            controlScheme = scheme
        };

        if (device is Gamepad gp)
            registeredGamepadIds.Add(gp.deviceId);
        else if (device is Keyboard)
            registeredKeyboardSchemes.Add(scheme);

        joinedCount++;
        Debug.Log($"[MultiplayerManager] Player {slotIndex + 1} joined — device: '{device.displayName}', scheme: '{scheme}'.");
    }

    // ─────────────────────────────────────────────
    // Assignment
    // ─────────────────────────────────────────────

    private void OnBothPlayersJoined()
    {
        assignmentDone = true;

        if (PROTOTYPE_MODE)
        {
            // Player 1 (slot 0, first to press) → Gun character.
            // Player 2 (slot 1, second to press) → Jump character.
            Debug.Log("[MultiplayerManager] Both joined. Assigning: slot 0 → Gun, slot 1 → Jump.");
            AssignDeviceToCharacter(joined[0], gunCharacter);
            AssignDeviceToCharacter(joined[1], jumpCharacter);
        }
        else
        {
            // TODO (lobby): Begin character selection here.
            // Call AssignDeviceToCharacter(slot, character) once both confirm.
        }
    }

    /// <summary>
    /// Assigns a joined player's device and control scheme to the PlayerInput
    /// component on the given character GameObject.
    ///
    /// For keyboard players: SwitchCurrentControlScheme is called with the
    /// scheme name only — no device argument — to avoid the Input System
    /// exclusively pairing Keyboard.current to this PlayerInput and stealing
    /// it from the other keyboard player.
    ///
    /// For gamepad players: the specific Gamepad device is passed so the
    /// Input System exclusively pairs it to this PlayerInput, preventing
    /// the other player's gamepad from controlling this character.
    /// </summary>
    private void AssignDeviceToCharacter(JoinedPlayer player, GameObject character)
    {
        PlayerInput pi = character.GetComponent<PlayerInput>();
        if (pi == null)
        {
            Debug.LogError($"[MultiplayerManager] No PlayerInput found on {character.name}!");
            return;
        }

        Debug.Log($"[MultiplayerManager] '{character.name}' — currently paired to: [{string.Join(", ", pi.user.pairedDevices)}], scheme: '{pi.currentControlScheme}'");

        if (player.device is Keyboard)
        {
            // Step 1: Switch scheme first. The scheme-name-only overload resets
            // the device list internally, so we must pair devices AFTER this call,
            // not before — otherwise the pairing gets wiped.
            pi.SwitchCurrentControlScheme(player.controlScheme);

            // Step 2: Pair the keyboard explicitly now that the scheme is set.
            // We do NOT use UnpairCurrentDevicesFromUser here because the scheme
            // switch already cleared the old devices in step 1.
            InputUser.PerformPairingWithDevice(Keyboard.current, pi.user);

            // Step 3: Gun player on keyboard also needs the mouse for aiming.
            // Only pair the mouse when the gun player is on a keyboard scheme —
            // gamepad players use the right stick and must not have the mouse
            // paired, otherwise Mouse.current reads could interfere with aiming.
            if (character == gunCharacter && Mouse.current != null)
                InputUser.PerformPairingWithDevice(Mouse.current, pi.user);
            // (This block is inside the keyboard branch, so mouse is never
            // paired for gamepad players — the else branch below handles them.)
        }
        else
        {
            // Gamepad: SwitchCurrentControlScheme with the specific device
            // atomically replaces the old pairing and activates the scheme.
            pi.SwitchCurrentControlScheme(player.controlScheme, player.device);
        }

        // If this is the gun character, tell GunController to re-evaluate its
        // aim mode now. onControlsChanged does not fire reliably on programmatic
        // reassignment, so we call UpdateAimMode() directly instead.
        if (character == gunCharacter)
        {
            GunController gunWeapon = character.GetComponentInChildren<GunController>();
            if (gunWeapon != null) gunWeapon.UpdateAimMode();
            else Debug.LogWarning("[MultiplayerManager] GunController not found — aim mode not updated.");
        }

        Debug.Log($"[MultiplayerManager] '{character.name}' assigned — scheme: '{player.controlScheme}', " +
                  $"device: '{player.device.displayName}', map: '{pi.currentActionMap?.name}', " +
                  $"paired devices after: [{string.Join(", ", pi.user.pairedDevices)}]");
    }

    // ─────────────────────────────────────────────
    // Lobby Stubs (Future Use)
    // ─────────────────────────────────────────────

    // When the lobby is ready:
    // 1. Set PROTOTYPE_MODE = false.
    // 2. In OnBothPlayersJoined(), show character selection UI.
    // 3. Call TryAssignCharacter(slotIndex, character) when a player confirms.
    // 4. Call AssignDeviceToCharacter(joined[slotIndex], character) inside it.

    /*
    public void TryAssignCharacter(int slotIndex, GameObject character)
    {
        if (slotIndex < 0 || slotIndex >= 2 || joined[slotIndex] == null) return;
        AssignDeviceToCharacter(joined[slotIndex], character);
    }
    */
}
