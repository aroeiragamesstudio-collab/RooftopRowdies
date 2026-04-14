using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class LobbyManager : MonoBehaviour
{
    [Header("Scene")]
    [Tooltip("Exact scene name in File > Build Settings.")]
    public string gameplaySceneName = "GameplayScene";

    [Header("Player 1 Slot UI")]
    public TMP_Text player1StatusText;       // e.g. "Press any button to join"
    public TMP_Text player1DeviceText;       // e.g. "WASD Keyboard"
    public TMP_Text player1CharacterText;    // e.g. "< GUN >"
    public GameObject player1ConfirmedBadge; // A "READY" badge, set active on confirm

    [Header("Player 2 Slot UI")]
    public TMP_Text player2StatusText;
    public TMP_Text player2DeviceText;
    public TMP_Text player2CharacterText;
    public GameObject player2ConfirmedBadge;

    [Header("Global UI")]
    public TMP_Text globalMessageText; // "Waiting for players...", "BOTH READY! Loading..."

    // ─────────────────────────────────────────────
    // Internal State
    // ─────────────────────────────────────────────

    // Maps a slot index (0 or 1) to all of its runtime data.
    private class LobbySlot
    {
        public int index;
        public InputDevice device;
        public string controlScheme;
        public MultiplayerSessionData.CharacterType selectedCharacter = MultiplayerSessionData.CharacterType.Gun;
        public bool joined = false;
        public bool confirmed = false;
    }

    private LobbySlot[] slots = new LobbySlot[2];
    private int joinedCount = 0;

    // Prevents the same device from joining twice in one frame.
    private HashSet<int> registeredDeviceIds = new HashSet<int>();
    private HashSet<string> registeredKeyboardSchemes = new HashSet<string>();

    // Character cycle order (only two options, but a list makes it easy to expand).
    private static readonly MultiplayerSessionData.CharacterType[] CharacterCycle =
    {
        MultiplayerSessionData.CharacterType.Gun,
        MultiplayerSessionData.CharacterType.Jump
    };

    // ─────────────────────────────────────────────
    // Unity Lifecycle
    // ─────────────────────────────────────────────

    private void Awake()
    {
        slots[0] = new LobbySlot { index = 0 };
        slots[1] = new LobbySlot { index = 1 };

        // If session data exists from a previous round, reset it.
        if (MultiplayerSessionData.Instance != null)
            MultiplayerSessionData.Instance.Reset();
    }

    private void Start()
    {
        RefreshAllUI();
    }

    private void Update()
    {
        DetectJoin();
        HandleNavigation();
    }

    // ─────────────────────────────────────────────
    // JOIN DETECTION
    // Every frame, check for any new button press on unclaimed devices.
    // ─────────────────────────────────────────────

    private void DetectJoin()
    {
        if (joinedCount >= 2) return;

        // ── Gamepads ──────────────────────────────────────────────────────────
        foreach (Gamepad gp in Gamepad.all)
        {
            if (registeredDeviceIds.Contains(gp.deviceId)) continue;
            if (!AnyGamepadButtonPressed(gp)) continue;

            RegisterSlot(gp, "Gamepad");
        }

        // ── Keyboard WASD ─────────────────────────────────────────────────────
        if (Keyboard.current != null && !registeredKeyboardSchemes.Contains("WASD"))
        {
            if (Keyboard.current.wKey.wasPressedThisFrame)
                RegisterSlot(Keyboard.current, "WASD");
        }

        // ── Keyboard Arrows ───────────────────────────────────────────────────
        if (Keyboard.current != null && !registeredKeyboardSchemes.Contains("Arrows"))
        {
            if (Keyboard.current.upArrowKey.wasPressedThisFrame)
                RegisterSlot(Keyboard.current, "Arrows");
        }
    }

    private bool AnyGamepadButtonPressed(Gamepad gp)
    {
        return gp.buttonSouth.wasPressedThisFrame ||
               gp.buttonNorth.wasPressedThisFrame ||
               gp.buttonEast.wasPressedThisFrame ||
               gp.buttonWest.wasPressedThisFrame ||
               gp.startButton.wasPressedThisFrame ||
               gp.leftShoulder.wasPressedThisFrame ||
               gp.rightShoulder.wasPressedThisFrame;
    }

    private void RegisterSlot(InputDevice device, string scheme)
    {
        if (joinedCount >= 2) return;

        int slotIndex = joinedCount;
        slots[slotIndex].device = device;
        slots[slotIndex].controlScheme = scheme;
        slots[slotIndex].joined = true;
        slots[slotIndex].confirmed = false;
        // Default: first joined picks Gun, second picks Jump.
        slots[slotIndex].selectedCharacter = slotIndex == 0
            ? MultiplayerSessionData.CharacterType.Gun
            : MultiplayerSessionData.CharacterType.Jump;

        if (device is Gamepad gp)
            registeredDeviceIds.Add(gp.deviceId);
        else
            registeredKeyboardSchemes.Add(scheme);

        joinedCount++;

        Debug.Log($"[Lobby] Player {slotIndex + 1} joined — scheme: {scheme}, device: {device.displayName}");
        RefreshSlotUI(slotIndex);

        if (joinedCount == 1 && globalMessageText != null)
            globalMessageText.text = "Waiting for Player 2...";
    }

    // ─────────────────────────────────────────────
    // NAVIGATION
    // Each joined slot polls its own device for L/R, confirm, and back.
    // ─────────────────────────────────────────────

    private void HandleNavigation()
    {
        for (int i = 0; i < 2; i++)
        {
            LobbySlot slot = slots[i];
            if (!slot.joined) continue;

            bool leftPressed = ReadLeft(slot);
            bool rightPressed = ReadRight(slot);
            bool confirmPressed = ReadConfirm(slot);
            bool backPressed = ReadBack(slot);

            // ── Character selection ───────────────────────────────────────────
            if (!slot.confirmed && (leftPressed || rightPressed))
            {
                CycleCharacter(slot, rightPressed ? 1 : -1);
            }

            // ── Confirm ───────────────────────────────────────────────────────
            if (!slot.confirmed && confirmPressed)
            {
                TryConfirm(slot);
            }

            // ── Back (unconfirm or leave) ─────────────────────────────────────
            if (backPressed)
            {
                if (slot.confirmed)
                {
                    slot.confirmed = false;
                    RefreshSlotUI(i);
                }
                else if (slot.joined && joinedCount > 0)
                {
                    LeaveSlot(slot);
                }
            }
        }
    }

    // ─────────────────────────────────────────────
    // DEVICE-SPECIFIC INPUT READERS
    // Each scheme reads different physical keys for navigation.
    // Gamepad readers work for any Gamepad by casting slot.device.
    // ─────────────────────────────────────────────

    private bool ReadLeft(LobbySlot slot)
    {
        return slot.controlScheme switch
        {
            "WASD" => Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame,
            "Arrows" => Keyboard.current != null && Keyboard.current.leftArrowKey.wasPressedThisFrame,
            "Gamepad" => slot.device is Gamepad gp && gp.dpad.left.wasPressedThisFrame,
            _ => false
        };
    }

    private bool ReadRight(LobbySlot slot)
    {
        return slot.controlScheme switch
        {
            "WASD" => Keyboard.current != null && Keyboard.current.dKey.wasPressedThisFrame,
            "Arrows" => Keyboard.current != null && Keyboard.current.rightArrowKey.wasPressedThisFrame,
            "Gamepad" => slot.device is Gamepad gp && gp.dpad.right.wasPressedThisFrame,
            _ => false
        };
    }

    private bool ReadConfirm(LobbySlot slot)
    {
        return slot.controlScheme switch
        {
            "WASD" => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame,
            "Arrows" => Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame,
            "Gamepad" => slot.device is Gamepad gp && gp.buttonSouth.wasPressedThisFrame,
            _ => false
        };
    }

    private bool ReadBack(LobbySlot slot)
    {
        return slot.controlScheme switch
        {
            "WASD" => Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame,
            "Arrows" => Keyboard.current != null && Keyboard.current.rightShiftKey.wasPressedThisFrame,
            "Gamepad" => slot.device is Gamepad gp && gp.buttonEast.wasPressedThisFrame,
            _ => false
        };
    }

    // ─────────────────────────────────────────────
    // CHARACTER CYCLING
    // ─────────────────────────────────────────────

    private void CycleCharacter(LobbySlot slot, int direction)
    {
        int currentIndex = System.Array.IndexOf(CharacterCycle, slot.selectedCharacter);
        int nextIndex = (currentIndex + direction + CharacterCycle.Length) % CharacterCycle.Length;
        slot.selectedCharacter = CharacterCycle[nextIndex];

        Debug.Log($"[Lobby] Player {slot.index + 1} selected: {slot.selectedCharacter}");
        RefreshSlotUI(slot.index);
    }

    // ─────────────────────────────────────────────
    // CONFIRMATION
    // ─────────────────────────────────────────────

    private void TryConfirm(LobbySlot slot)
    {
        // Check if the other player has already chosen the same character.
        int otherIndex = slot.index == 0 ? 1 : 0;
        LobbySlot other = slots[otherIndex];

        if (other.joined && other.confirmed && other.selectedCharacter == slot.selectedCharacter)
        {
            // Flash an error message — characters must be different.
            if (globalMessageText != null)
                globalMessageText.text = "Each player must choose a different character!";
            return;
        }

        slot.confirmed = true;
        Debug.Log($"[Lobby] Player {slot.index + 1} confirmed: {slot.selectedCharacter}");
        RefreshSlotUI(slot.index);
        CheckBothReady();
    }

    private void CheckBothReady()
    {
        if (!slots[0].joined || !slots[1].joined) return;
        if (!slots[0].confirmed || !slots[1].confirmed) return;
        if (slots[0].selectedCharacter == slots[1].selectedCharacter) return;

        if (globalMessageText != null)
            globalMessageText.text = "BOTH READY! Loading game...";

        WriteSessionDataAndLoad();
    }

    // ─────────────────────────────────────────────
    // LEAVING
    // ─────────────────────────────────────────────

    private void LeaveSlot(LobbySlot slot)
    {
        int slotIndex = slot.index;

        if (slot.device is Gamepad gp)
            registeredDeviceIds.Remove(gp.deviceId);
        else if (slot.device is Keyboard)
            registeredKeyboardSchemes.Remove(slot.controlScheme);

        slot.device = null;
        slot.controlScheme = null;
        slot.joined = false;
        slot.confirmed = false;
        slot.selectedCharacter = MultiplayerSessionData.CharacterType.None;

        joinedCount = Mathf.Max(0, joinedCount - 1);

        // If the slot that left was slot 0 and slot 1 was filled, shift slot 1 down.
        // This keeps the "first joined = slot 0" invariant.
        if (slotIndex == 0 && slots[1].joined)
        {
            slots[0].device = slots[1].device;
            slots[0].controlScheme = slots[1].controlScheme;
            slots[0].joined = true;
            slots[0].confirmed = slots[1].confirmed;
            slots[0].selectedCharacter = slots[1].selectedCharacter;

            slots[1].device = null;
            slots[1].controlScheme = null;
            slots[1].joined = false;
            slots[1].confirmed = false;
            slots[1].selectedCharacter = MultiplayerSessionData.CharacterType.None;

            joinedCount = 1;
        }

        RefreshAllUI();
    }

    // ─────────────────────────────────────────────
    // SESSION DATA WRITE + SCENE LOAD
    // ─────────────────────────────────────────────

    private void WriteSessionDataAndLoad()
    {
        // Ensure the singleton exists. If it's not already in the scene,
        // create it now so DontDestroyOnLoad keeps it alive.
        if (MultiplayerSessionData.Instance == null)
        {
            GameObject go = new GameObject("MultiplayerSessionData");
            go.AddComponent<MultiplayerSessionData>();
        }

        MultiplayerSessionData data = MultiplayerSessionData.Instance;
        data.Reset();

        data.players[0].device = slots[0].device;
        data.players[0].controlScheme = slots[0].controlScheme;
        data.players[0].character = slots[0].selectedCharacter;
        data.players[0].confirmed = true;

        data.players[1].device = slots[1].device;
        data.players[1].controlScheme = slots[1].controlScheme;
        data.players[1].character = slots[1].selectedCharacter;
        data.players[1].confirmed = true;

        Debug.Log($"[Lobby] Loading scene '{gameplaySceneName}'. " +
                  $"P1: {data.players[0].controlScheme} → {data.players[0].character}, " +
                  $"P2: {data.players[1].controlScheme} → {data.players[1].character}");

        SceneManager.LoadScene(gameplaySceneName);

        if (MenuController.instance != null)
        {
            MenuController.instance.GoToGame();
        }
    }

    // ─────────────────────────────────────────────
    // UI REFRESH
    // ─────────────────────────────────────────────

    private void RefreshSlotUI(int slotIndex)
    {
        LobbySlot slot = slots[slotIndex];

        TMP_Text statusText = slotIndex == 0 ? player1StatusText : player2StatusText;
        TMP_Text deviceText = slotIndex == 0 ? player1DeviceText : player2DeviceText;
        TMP_Text charText = slotIndex == 0 ? player1CharacterText : player2CharacterText;
        GameObject readyBadge = slotIndex == 0 ? player1ConfirmedBadge : player2ConfirmedBadge;

        if (!slot.joined)
        {
            if (statusText != null) statusText.text = slotIndex == 0
                ? "Press W or UP or Gamepad button to join"
                : "Waiting for Player 2...";
            if (deviceText != null) deviceText.text = "";
            if (charText != null) charText.text = "";
            if (readyBadge != null) readyBadge.SetActive(false);
            return;
        }

        // ── Device label ──────────────────────────────────────────────────────
        string deviceLabel = slot.controlScheme switch
        {
            "WASD" => "Keyboard (WASD)",
            "Arrows" => "Keyboard (Arrows)",
            "Gamepad" => slot.device != null ? slot.device.displayName : "Gamepad",
            _ => "Unknown"
        };

        if (statusText != null) statusText.text = slot.confirmed ? "READY!" : "Choose your character";
        if (deviceText != null) deviceText.text = deviceLabel;

        // ── Character label ───────────────────────────────────────────────────
        if (charText != null)
        {
            bool isFirst = slot.selectedCharacter == CharacterCycle[0];
            bool isLast = slot.selectedCharacter == CharacterCycle[CharacterCycle.Length - 1];
            string left = isFirst ? "  " : "< ";
            string right = isLast ? "  " : " >";
            charText.text = $"{left}{CharacterName(slot.selectedCharacter)}{right}";
        }

        if (readyBadge != null) readyBadge.SetActive(slot.confirmed);
    }

    private void RefreshAllUI()
    {
        RefreshSlotUI(0);
        RefreshSlotUI(1);

        if (globalMessageText != null)
        {
            if (joinedCount == 0)
                globalMessageText.text = "Press a button to join!";
            else if (joinedCount == 1)
                globalMessageText.text = "Waiting for Player 2...";
            else
                globalMessageText.text = "Both players joined. Select characters and confirm!";
        }
    }

    private string CharacterName(MultiplayerSessionData.CharacterType type)
    {
        return type switch
        {
            MultiplayerSessionData.CharacterType.Gun => "GUN",
            MultiplayerSessionData.CharacterType.Jump => "JUMP",
            _ => "???"
        };
    }
}
