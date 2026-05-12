using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class SettingsMenu : MonoBehaviour
{
    public static SettingsMenu instance { get; private set; }

    private SettingsData data;

    [Header("Audio Mixer")]
    [SerializeField] AudioMixer audioMixer;

    [Header("Tab System")]
    [Tooltip("All tab content panels - order matches the tab buttons.")]
    [SerializeField] List<GameObject> tabPanels;
    [Tooltip("Tab button images so you can visually highlight the active one.")]
    [SerializeField] List<Button> tabButtons;
    [SerializeField] Color activeTabColor = Color.white;
    [SerializeField] Color inactiveTabColor = new Color(0.6f, 0.6f, 0.6f);

    [Header("Audio UI")]
    [SerializeField] Slider masterVolumeSlider;
    [SerializeField] Slider musicVolumeSlider;
    [SerializeField] Slider sfxVolumeSlider;

    [Header("Video UI")]
    [SerializeField] Toggle fullscreenToggle;

    [Header("Gameplay UI")]
    [SerializeField] Toggle rumbleToggle;
    [SerializeField] Toggle cameraShakeToggle;
    [SerializeField] Toggle colorblindToggle;

    [HideInInspector] public bool isFullscreen;
    [HideInInspector] public bool isRumbleEnabled;
    [HideInInspector] public bool isCameraShakeEnabled;
    [HideInInspector] public bool isColorblindMode;

    int activeTabIndex = 0;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        data = SettingsRepository.Load();
        LoadAllSettings();
    }

    private void Start()
    {
        ApplyAllSettingsToUI();
        SwitchTab(0);
    }

    /// <summary>
    /// Chame isso de cada botão de tab no evento OnClick no inspector,
    /// Passando o index da tab (0 = Audio, 1 = Video, 2 = Gameplay, etc)
    /// </summary>
    public void SwitchTab(int index)
    {
        if (index < 0 || index >= tabPanels.Count) return;

        for (int i = 0; i < tabPanels.Count; i++)
        {
            tabPanels[i].SetActive(i == index);

            if (tabButtons != null && i < tabButtons.Count)
            {
                ColorBlock cb = tabButtons[i].colors;
                cb.normalColor = (i == index) ? activeTabColor : inactiveTabColor;
                tabButtons[i].colors = cb;
            }
        }

        activeTabIndex = index;
    }

    #region Save&Load

    void LoadAllSettings()
    {
        // Load with sensible defaults if no key exists yet
        float masterVol = data.masterVolume;
        float musicVol = data.musicVolume;
        float sfxVol = data.sfxVolume;
        isFullscreen = data.fullscreen;
        isRumbleEnabled = data.rumbleEnabled;
        isCameraShakeEnabled = data.cameraShakeEnabled;
        isColorblindMode = data.colorblindMode;

        // Apply to actual systems immediately
        audioMixer.SetFloat("MasterVolume", masterVol);
        audioMixer.SetFloat("MusicVolume", musicVol);
        audioMixer.SetFloat("SFXVolume", sfxVol);
        Screen.fullScreen = isFullscreen;
    }

    void ApplyAllSettingsToUI()
    {
        // Sync UI controls to reflect the loaded values
        // Guards against null refs if you haven't wired every field yet
        if (masterVolumeSlider != null)
        {
            audioMixer.GetFloat("MasterVolume", out float mv);
            masterVolumeSlider.value = mv;
        }
        if (musicVolumeSlider != null)
        {
            audioMixer.GetFloat("MusicVolume", out float mu);
            musicVolumeSlider.value = mu;
        }
        if (sfxVolumeSlider != null)
        {
            audioMixer.GetFloat("SFXVolume", out float sfx);
            sfxVolumeSlider.value = sfx;
        }
        if (fullscreenToggle != null)
            fullscreenToggle.isOn = isFullscreen;
        if (rumbleToggle != null)
            rumbleToggle.isOn = isRumbleEnabled;
        if (cameraShakeToggle != null)
            cameraShakeToggle.isOn = isCameraShakeEnabled;
        if (colorblindToggle != null)
            colorblindToggle.isOn = isColorblindMode;
    }

    public void SaveAndClose()
    {
        SettingsRepository.Save(data);
        MenuController.instance.CloseOptions();
    }

    #endregion

    #region Audio

    public void SetMasterVolume(float volume)
    {
        data.masterVolume = volume;
        audioMixer.SetFloat("MasterVolume", volume);
    }

    public void SetMusicVolume(float volume)
    {
        data.musicVolume = volume;
        audioMixer.SetFloat("MusicVolume", volume);
    }

    public void SetSFXVolume(float volume)
    {
        data.sfxVolume = volume;
        audioMixer.SetFloat("SFXVolume", volume);
    }

    #endregion

    #region Video

    public void SetFullscreen(bool _isFullscreen)
    {
        data.fullscreen = _isFullscreen;
        Screen.fullScreen = _isFullscreen;
        isFullscreen = _isFullscreen;
    }

    public void SetResolution(int resolutionIndex, Resolution resolution)
    {
        data.resolutionIndex = resolutionIndex;
        Screen.SetResolution(resolution.width, resolution.height, isFullscreen);
    }

    #endregion

    #region Gameplay/Accessbility

    public void SetRumble(bool enabled)
    {
        data.rumbleEnabled = enabled;
        isRumbleEnabled = enabled;
        // Chamar RumbleManager.instance.SetEnabled(enabled) ou depois colocar que chame esse metodo
    }
    public void SetCameraShake(bool enabled)
    {
        data.cameraShakeEnabled = enabled;
        isCameraShakeEnabled = enabled;
        // Future: CameraShakeManager.Instance.SetEnabled(enabled)
    }

    public void SetColorblindMode(bool enabled)
    {
        data.colorblindMode = enabled;
        isColorblindMode = enabled;
        // Future: pass to a PostProcessing or shader global
    }

    #endregion
}
