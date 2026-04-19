using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ResolutionManager : MonoBehaviour
{
    [SerializeField] TMP_Dropdown resolutionDropdown;

    private List<Resolution> validResolutions = new();

    private void Start()
    {
        PopulateDropdown();
    }

    private void PopulateDropdown()
    {
        Resolution[] all = Screen.resolutions;
        validResolutions.Clear();
        resolutionDropdown.ClearOptions();

        var currentRate = Screen.currentResolution.refreshRateRatio;
        var options = new List<string>();
        int currentIndex = 0;

        foreach (var res in all)
        {
            if (res.refreshRateRatio.value != currentRate.value) continue;

            validResolutions.Add(res);

            options.Add($"{res.width}x{res.height} {res.refreshRateRatio} Hz");

            if (res.width == Screen.width && res.height == Screen.height)
                currentIndex = validResolutions.Count - 1;
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.value = currentIndex;
        resolutionDropdown.RefreshShownValue();

        // Restore saved resolution index if it exists and is valid
        if (PlayerPrefs.HasKey("ResolutionIndex"))
        {
            int saved = PlayerPrefs.GetInt("ResolutionIndex");
            if (saved < validResolutions.Count)
            {
                resolutionDropdown.value = saved;
                resolutionDropdown.RefreshShownValue();
            }
        }
    }

    // Called by the dropdown's OnValueChanged event in the Inspector
    public void OnDropdownChanged(int index)
    {
        if (index < 0 || index >= validResolutions.Count) return;

        if (SettingsMenu.instance != null)
            SettingsMenu.instance.SetResolution(index, validResolutions[index]);
        else
            Screen.SetResolution(validResolutions[index].width,
                                 validResolutions[index].height,
                                 Screen.fullScreen);
    }
}
