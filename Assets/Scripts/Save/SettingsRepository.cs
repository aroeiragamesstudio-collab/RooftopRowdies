using UnityEngine;

public static class SettingsRepository
{
    private const string KEY = "GameSettings";

    public static SettingsData Load()
    {
        if (PlayerPrefs.HasKey(KEY))
        {
            return JsonUtility.FromJson<SettingsData>(PlayerPrefs.GetString(KEY));
        }
        return new SettingsData(); // default
    }

    public static void Save(SettingsData data)
    {
        PlayerPrefs.SetString(KEY, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }
}
