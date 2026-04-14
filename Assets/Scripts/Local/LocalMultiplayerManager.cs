using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;
using UnityEngine.SceneManagement;

public class LocalMultiplayerManager : MonoBehaviour
{
    [Header("Characters (pre-placed in scene)")]
    [Tooltip("The GameObject with GunCharacterController and PlayerInput.")]
    public GameObject gunCharacter;

    [Tooltip("The GameObject with JumpCharacterController and PlayerInput.")]
    public GameObject jumpCharacter;

    [Header("Fallback / Prototype Mode")]
    [Tooltip("Used when no session data is available (e.g. starting directly from this scene in the Editor).")]
    public bool PROTOTYPE_MODE = true;

    // ─────────────────────────────────────────────
    // Unity Lifecycle
    // ─────────────────────────────────────────────

    private void Start()
    {
        // Step 1: Wire cross-references between the characters.
        // This must happen before input assignment so the characters can
        // communicate with each other from the first frame.
        WireCrossReferences();

        // Step 2: Apply input assignments.
        MultiplayerSessionData data = MultiplayerSessionData.Instance;

        if (data != null && data.IsReady)
        {
            Debug.Log("[LocalMultiplayerManager] Session data found. Applying lobby assignments.");
            ApplySessionData(data);

            // Step 3: Destroy the session data object — it's been consumed.
            // The next time the lobby loads, a fresh one will be created.
            Destroy(data.gameObject);
        }
        else
        {
            if (PROTOTYPE_MODE)
            {
                Debug.LogWarning("[LocalMultiplayerManager] No session data found. Falling back to PROTOTYPE_MODE.");
                ApplyPrototypeFallback();
            }
            else
            {
                Debug.LogError("[LocalMultiplayerManager] No session data found and PROTOTYPE_MODE is off. " +
                               "Did you start the game from the lobby scene?");
            }
        }
    }

    // ─────────────────────────────────────────────
    // Cross-Reference Wiring
    // ─────────────────────────────────────────────

    /// <summary>
    /// Directly assigns each controller's reference to the other character
    /// and connects both DistanceJoint2D components.
    /// This runs once at Start since both objects are already in the scene.
    /// </summary>
    private void WireCrossReferences()
    {
        if (gunCharacter == null || jumpCharacter == null)
        {
            Debug.LogError("[LocalMultiplayerManager] gunCharacter or jumpCharacter is not assigned!");
            return;
        }

        GunCharacterController gunCtrl = gunCharacter.GetComponent<GunCharacterController>();
        JumpCharacterController jumpCtrl = jumpCharacter.GetComponent<JumpCharacterController>();
        GunController gunWeapon = gunCharacter.GetComponentInChildren<GunController>();

        Rigidbody2D gunRb = gunCharacter.GetComponent<Rigidbody2D>();
        Rigidbody2D jumpRb = jumpCharacter.GetComponent<Rigidbody2D>();
        DistanceJoint2D gunJoint = gunCharacter.GetComponent<DistanceJoint2D>();
        DistanceJoint2D jumpJoint = jumpCharacter.GetComponent<DistanceJoint2D>();

        if (gunCtrl != null) gunCtrl.paws = jumpCtrl;
        if (jumpCtrl != null) jumpCtrl.porky = gunCtrl;
        if (gunWeapon != null) gunWeapon.jumpChar = jumpCtrl;

        if (gunJoint != null && jumpRb != null) gunJoint.connectedBody = jumpRb;
        if (jumpJoint != null && gunRb != null) jumpJoint.connectedBody = gunRb;

        Debug.Log("[LocalMultiplayerManager] Cross-references wired.");
    }

    // ─────────────────────────────────────────────
    // Session Data Application
    // ─────────────────────────────────────────────

    /// <summary>
    /// Reads MultiplayerSessionData and assigns each player's device to the
    /// character they chose in the lobby.
    /// </summary>
    private void ApplySessionData(MultiplayerSessionData data)
    {
        foreach (var player in data.players)
        {
            if (player.character == MultiplayerSessionData.CharacterType.None) continue;

            GameObject target = player.character == MultiplayerSessionData.CharacterType.Gun
                ? gunCharacter
                : jumpCharacter;

            AssignDeviceToCharacter(player.device, player.controlScheme, target);
        }
    }

    /// <summary>
    /// Prototype fallback: assigns WASD to Gun and Arrows to Jump without
    /// requiring the lobby. Used for in-editor testing of the gameplay scene.
    /// </summary>
    private void ApplyPrototypeFallback()
    {
        if (Keyboard.current == null)
        {
            Debug.LogError("[LocalMultiplayerManager] No keyboard detected in prototype fallback.");
            return;
        }

        AssignDeviceToCharacter(Keyboard.current, "WASD", gunCharacter);
        AssignDeviceToCharacter(Keyboard.current, "Arrows", jumpCharacter);
    }

    // ─────────────────────────────────────────────
    // Device Assignment
    // ─────────────────────────────────────────────

    /// <summary>
    /// Assigns a device and control scheme to the PlayerInput on the given character.
    ///
    /// For keyboard players: SwitchCurrentControlScheme is called with the scheme name
    /// only. This avoids exclusive pairing of the physical Keyboard device, which would
    /// break the other keyboard player. The bindings themselves filter by key (WASD vs Arrows).
    ///
    /// For gamepad players: SwitchCurrentControlScheme is called with both the scheme name
    /// and the specific Gamepad device, which exclusively pairs that gamepad to this
    /// PlayerInput so the other player's gamepad cannot control this character.
    /// </summary>
    private void AssignDeviceToCharacter(InputDevice device, string scheme, GameObject character)
    {
        PlayerInput pi = character.GetComponent<PlayerInput>();
        if (pi == null)
        {
            Debug.LogError($"[LocalMultiplayerManager] No PlayerInput on {character.name}!");
            return;
        }

        if (device is Keyboard)
        {
            // Scheme-only: no device passed → no exclusive pairing.
            pi.SwitchCurrentControlScheme(scheme);

            // Pair keyboard explicitly now that the scheme is set.
            // (The scheme switch above resets device pairings, so we re-pair after.)
            InputUser.PerformPairingWithDevice(Keyboard.current, pi.user);

            // Gun character on keyboard also needs the mouse for aiming.
            if (character == gunCharacter && Mouse.current != null)
                InputUser.PerformPairingWithDevice(Mouse.current, pi.user);
        }
        else if (device is Gamepad gp)
        {
            // Scheme + device: exclusively pairs this gamepad to this PlayerInput.
            pi.SwitchCurrentControlScheme(scheme, gp);
        }
        else
        {
            Debug.LogWarning($"[LocalMultiplayerManager] Unrecognized device type: {device?.GetType().Name}. " +
                              "Applying scheme only.");
            pi.SwitchCurrentControlScheme(scheme);
        }

        // If this is the gun character, tell GunController to re-check its aim mode.
        // onControlsChanged does not fire reliably on programmatic assignment.
        if (character == gunCharacter)
        {
            GunController gunWeapon = character.GetComponentInChildren<GunController>();
            if (gunWeapon != null) gunWeapon.UpdateAimMode();
        }

        Debug.Log($"[LocalMultiplayerManager] '{character.name}' assigned — " +
                  $"scheme: '{scheme}', device: '{device?.displayName}', " +
                  $"map: '{pi.currentActionMap?.name}'");
    }

    // ─────────────────────────────────────────────
    // Public Utility
    // ─────────────────────────────────────────────

    /// <summary>
    /// Returns to the lobby/menu scene and resets session data.
    /// Call this from a pause menu or game-over screen.
    /// </summary>
    public void ReturnToMenu(string menuSceneName)
    {
        if (MultiplayerSessionData.Instance != null)
            MultiplayerSessionData.Instance.Reset();

        SceneManager.LoadScene(menuSceneName);
    }
}
