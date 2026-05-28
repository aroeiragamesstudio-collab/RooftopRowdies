using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// MenuController da cena de menu. AGORA integrado com UIPanelStack:
/// - Telas de UI são empilhadas em vez de ligadas/desligadas via SetActive direto.
/// - Isso faz com que CloseOptions() funcione tanto no menu principal quanto no
///   menu de pausa do gameplay, voltando para o painel correto em cada contexto.
///
/// Mudanças principais em relação à versão anterior:
///   1. Start() agora popula a pilha com o MainMenu inicialmente (push em vez de SetActive).
///   2. OpenOptions/CloseOptions usam Push/Pop.
///   3. GoToLobby/GoToGame usam Push/Clear, dependendo do caso.
///   4. BackToMenu reseta a pilha pra um estado limpo.
///
/// Importante: o painel de Options precisa ter um campo "firstSelectedInOptions"
/// preenchido (a primeira tab do menu de configurações, por exemplo) para que a
/// navegação por gamepad/teclado funcione direito quando ele abre.
/// </summary>
public class MenuController : MonoBehaviour
{
    public static MenuController instance;

    [Header("Telas")]
    [SerializeField] GameObject mainMenu;
    [SerializeField] GameObject confirmMessage;
    [SerializeField] GameObject optionsMenu;
    [SerializeField] GameObject lobby;

    [Header("Primeiros itens de cada tela (para navegação por keyboard/gamepad)")]
    [Tooltip("Primeiro botão do menu principal (geralmente o Start).")]
    [SerializeField] GameObject firstSelectedInMainMenu;
    [Tooltip("Primeiro botão da tela de confirmação de saída (geralmente Cancel, por segurança).")]
    [SerializeField] GameObject firstSelectedInConfirm;
    [Tooltip("Primeiro item das opções (geralmente a primeira tab).")]
    [SerializeField] GameObject firstSelectedInOptions;

    [Header("Botões")]
    [SerializeField] Button startBtn;
    [SerializeField] Button optionsBtn;
    [SerializeField] Button quitBtn;
    [SerializeField] Button confirmQuitBtn;
    [SerializeField] Button cancelBtn;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(this);
    }

    void Start()
    {
        UIPanelStack.ForceReset();

        // Estado inicial: todos desativados, depois empilhamos o main menu
        // (a pilha vai cuidar de ativar e selecionar).
        if (confirmMessage != null) confirmMessage.SetActive(false);
        if (lobby != null) lobby.SetActive(false);
        if (optionsMenu != null) optionsMenu.SetActive(false);
        if (mainMenu != null) mainMenu.SetActive(false);

        startBtn.onClick.AddListener(GoToLobby);
        quitBtn.onClick.AddListener(QuitBtn);
        confirmQuitBtn.onClick.AddListener(ConfirmQuit);
        cancelBtn.onClick.AddListener(Cancel);
        optionsBtn.onClick.AddListener(OpenOptions);

        if (mainMenu != null)
            UIPanelStack.Push(mainMenu, firstSelectedInMainMenu);
    }

    // Add to MenuController.cs
    private void Update()
    {
        // If the EventSystem has no selection but we have an active panel,
        // force-select the first interactable button. This handles the
        // return-from-gameplay case where the selection was lost during
        // the scene transition.
        if (EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject == null
            && mainMenu != null
            && mainMenu.activeInHierarchy)
        {
            var selectable = mainMenu.GetComponentInChildren<UnityEngine.UI.Selectable>(false);
            if (selectable != null && selectable.IsInteractable())
            {
                EventSystem.current.SetSelectedGameObject(selectable.gameObject);
            }
        }
    }

    public void GoToLobby()
    {
        // O lobby substitui o main menu — não é uma sobreposição. Limpa a pilha
        // e empilha o lobby como nova base. (LobbyManager faz seu próprio polling
        // de input, então o firstSelected pode ser null.)
        UIPanelStack.Clear();
        if (lobby != null)
            UIPanelStack.Push(lobby, null);
    }

    public void GoToGame()
    {
        // Indo pra cena de gameplay — limpa toda a UI do menu para que ela
        // não fique desenhada por cima da gameplay.
        UIPanelStack.Clear();
    }

    public void BackToMenu()
    {
        // Voltando do gameplay para o menu — reseta tudo e empilha o main menu novamente.
        UIPanelStack.Clear();
        UIPanelStack.Push(mainMenu, firstSelectedInMainMenu);
    }

    public void OpenOptions()
    {
        if (optionsMenu == null)
        {
            Debug.LogWarning("[MenuController] optionsMenu não está atribuído.");
            return;
        }
        // Push: o painel anterior (pause OU main menu) é guardado e desativado;
        // options ativa em cima e foca a primeira tab.
        UIPanelStack.Push(optionsMenu, firstSelectedInOptions);
    }

    public void CloseOptions()
    {
        // Pop: options desativa e o painel anterior reativa, com a seleção que
        // estava nele (geralmente o botão "Options" que o usuário clicou).
        UIPanelStack.Pop();
    }

    public void QuitBtn()
    {
        if (confirmMessage == null)
        {
            Debug.LogWarning("[MenuController] confirmMessage não está atribuído.");
            return;
        }
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
        // Cancela a confirmação — pop volta ao main menu.
        confirmMessage.SetActive(true);
    }

    private void OnDestroy()
    {
        if (startBtn != null) startBtn.onClick.RemoveAllListeners();
        if (quitBtn != null) quitBtn.onClick.RemoveAllListeners();
        if (confirmQuitBtn != null) confirmQuitBtn.onClick.RemoveAllListeners();
        if (cancelBtn != null) cancelBtn.onClick.RemoveAllListeners();
        if (optionsBtn != null) optionsBtn.onClick.RemoveAllListeners();
    }
}