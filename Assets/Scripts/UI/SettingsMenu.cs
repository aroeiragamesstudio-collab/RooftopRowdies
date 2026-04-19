using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class SettingsMenu : MonoBehaviour
{
    public static SettingsMenu instance { get; private set; }

    // Chaves PlayerPrefs
    // Modificar depois para funcionar sem PlayerPrefs ou se não há necessidade
    private const string KEY_MASTER_VOL = "MasterVolume";
    private const string KEY_MUSIC_VOL = "MusicVolume";
    private const string KEY_SFX_VOL = "SFXVolume";
    private const string KEY_FULLSCREEN = "Fullscreen";
    private const string KEY_RESOLUTION = "ResolutionIndex";
    private const string KEY_RUMBLE = "RumbleEnabled";
    private const string KEY_CAM_SHAKE = "CameraShake";
    private const string KEY_COLORBLIND = "ColorblindMode";

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
        if(instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

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

            if(tabButtons != null && i < tabButtons.Count)
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
        float masterVol = PlayerPrefs.GetFloat(KEY_MASTER_VOL, 0f);
        float musicVol = PlayerPrefs.GetFloat(KEY_MUSIC_VOL, 0f);
        float sfxVol = PlayerPrefs.GetFloat(KEY_SFX_VOL, 0f);
        isFullscreen = PlayerPrefs.GetInt(KEY_FULLSCREEN, 1) == 1;
        isRumbleEnabled = PlayerPrefs.GetInt(KEY_RUMBLE, 1) == 1;
        isCameraShakeEnabled = PlayerPrefs.GetInt(KEY_CAM_SHAKE, 1) == 1;
        isColorblindMode = PlayerPrefs.GetInt(KEY_COLORBLIND, 0) == 1;

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
        PlayerPrefs.Save();
        MenuController.instance.CloseOptions();
    }

    #endregion

    #region Audio

    public void SetMasterVolume(float volume)
    {
        audioMixer.SetFloat("MasterVolume", volume);
        PlayerPrefs.SetFloat(KEY_MASTER_VOL, volume);
    }

    public void SetMusicVolume(float volume)
    {
        audioMixer.SetFloat("MusicVolume", volume);
        PlayerPrefs.SetFloat(KEY_MUSIC_VOL, volume);
    }

    public void SetSFXVolume(float volume)
    {
        audioMixer.SetFloat("SFXVolume", volume);
        PlayerPrefs.SetFloat(KEY_SFX_VOL, volume);
    }

    #endregion

    #region Video

    public void SetFullscreen(bool _isFullscreen)
    {
        Screen.fullScreen = _isFullscreen;
        isFullscreen = _isFullscreen;
        PlayerPrefs.SetInt(KEY_FULLSCREEN, _isFullscreen ? 1 : 0);
    }

    public void SetResolution(int resolutionIndex, Resolution resolution)
    {
        Screen.SetResolution(resolution.width, resolution.height, isFullscreen);
        PlayerPrefs.SetInt(KEY_RESOLUTION, resolutionIndex);
    }

    #endregion

    #region Gameplay/Accessbility

    public void SetRumble(bool enabled)
    {
        isRumbleEnabled = enabled;
        PlayerPrefs.SetInt(KEY_RUMBLE, enabled ? 1 : 0);
        // Chamar RumbleManager.instance.SetEnabled(enabled) ou depois colocar que chame esse metodo
    }
    public void SetCameraShake(bool enabled)
    {
        isCameraShakeEnabled = enabled;
        PlayerPrefs.SetInt(KEY_CAM_SHAKE, enabled ? 1 : 0);
        // Future: CameraShakeManager.Instance.SetEnabled(enabled)
    }

    public void SetColorblindMode(bool enabled)
    {
        isColorblindMode = enabled;
        PlayerPrefs.SetInt(KEY_COLORBLIND, enabled ? 1 : 0);
        // Future: pass to a PostProcessing or shader global
    }

    #endregion
}
