using UnityEngine;
using UnityEngine.UI;

public class MenuController : MonoBehaviour
{
    public static MenuController instance;

    [Header("Telas")]
    [SerializeField] GameObject mainMenu;
    [SerializeField] GameObject confirmMessage;
    [SerializeField] GameObject optionsMenu;
    [SerializeField] GameObject lobby;

    [Header("Botões")]
    [SerializeField] Button startBtn;
    [SerializeField] Button optionsBtn;
    [SerializeField] Button quitBtn;
    [SerializeField] Button confirmQuitBtn;
    [SerializeField] Button cancelBtn;

    private void Awake()
    {
        if(instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(this);
    }


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        confirmMessage.SetActive(false);
        lobby.SetActive(false);
        startBtn.onClick.AddListener(GoToLobby);
        quitBtn.onClick.AddListener(QuitBtn);
        confirmQuitBtn.onClick.AddListener(ConfirmQuit);
        cancelBtn.onClick.AddListener(Cancel);
        optionsBtn.onClick.AddListener(OpenOptions);
    }

    public void GoToLobby()
    {
        lobby.SetActive(true);
        mainMenu.SetActive(false);
    }

    public void GoToGame()
    {
        lobby.SetActive(false);
        mainMenu.SetActive(false);
    }

    public void OpenOptions()
    {
        optionsMenu.SetActive(true);
        //mainMenu.SetActive(false);
    }

    public void CloseOptions()
    {
        optionsMenu.SetActive(false);
    }

    public void QuitBtn()
    {
        confirmMessage.SetActive(true);
    }

    public void ConfirmQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void Cancel()
    {
        confirmMessage.SetActive(false);
    }

    private void OnDestroy()
    {
        startBtn.onClick.RemoveAllListeners();
        quitBtn.onClick.RemoveAllListeners();
        confirmQuitBtn.onClick.RemoveAllListeners();
        cancelBtn.onClick.RemoveAllListeners();
        optionsBtn.onClick.RemoveAllListeners();
    }
}
