using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using System.Threading.Tasks;
using UnityEngine.UI;
using TMPro;

public class SimpleRelayManager : MonoBehaviour
{
    [Header("Главное меню")]
    public GameObject menuPanel;
    public TMP_InputField joinCodeInput;
    public TextMeshProUGUI statusText;
    public Button hostButton;
    public Button joinButton;

    [Header("Панель Настроек")]
    public GameObject settingsPanel;
    public Button openSettingsButton;
    public Button closeSettingsButton;
    public Button leaveButton;
    public TextMeshProUGUI settingsJoinCodeText;
    public Slider sensitivitySlider;

    [Header("Игровой интерфейс (Скрытие)")]
    public GameObject gameplayHUD;

    private string currentJoinCode;
    private bool isStarting = false;
    private bool gameStarted = false;
    private bool isServicesInitialized = false;
    private bool isSubscribed = false;

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void TrySubscribe()
    {
        if (isSubscribed || NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!isSubscribed || NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        isSubscribed = false;
    }

    async void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        SetUIActive(true);
        SetButtonsInteractable(false);
        if (settingsPanel != null) settingsPanel.SetActive(false);

        if (gameplayHUD != null) gameplayHUD.SetActive(false);

        if (statusText != null) statusText.text = "Подключение к сервисам...";

        TrySubscribe();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        await InitializeUnityServices();

        if (isServicesInitialized)
        {
            SetupUI();
        }
    }

    private void SetupUI()
    {
        SetUIActive(true);

        if (joinCodeInput != null)
        {
            joinCodeInput.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
            joinCodeInput.characterLimit = 12;
            joinCodeInput.onEndEdit.RemoveAllListeners();
            joinCodeInput.onEndEdit.AddListener((value) => { joinCodeInput.text = value.ToUpper(); });
        }

        if (hostButton != null)
        {
            hostButton.onClick.RemoveAllListeners();
            hostButton.onClick.AddListener(CreateRelayGame);
        }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(JoinRelayGame);
        }

        if (openSettingsButton != null)
        {
            openSettingsButton.onClick.RemoveAllListeners();
            openSettingsButton.onClick.AddListener(() =>
            {
                if (settingsPanel != null) settingsPanel.SetActive(true);
                if (openSettingsButton != null) openSettingsButton.gameObject.SetActive(false);
                SetGameplayControlsOnlyActive(false);
            });
        }

        if (closeSettingsButton != null)
        {
            closeSettingsButton.onClick.RemoveAllListeners();
            closeSettingsButton.onClick.AddListener(() =>
            {
                if (settingsPanel != null) settingsPanel.SetActive(false);
                if (openSettingsButton != null) openSettingsButton.gameObject.SetActive(true);
                SetGameplayControlsOnlyActive(true);
            });
        }

        if (leaveButton != null)
        {
            leaveButton.onClick.RemoveAllListeners();
            leaveButton.onClick.AddListener(LeaveGame);
        }

        if (sensitivitySlider != null)
        {
            sensitivitySlider.minValue = 0.1f;
            sensitivitySlider.maxValue = 3.0f;
            sensitivitySlider.value = PlayerPrefs.GetFloat("Sensitivity", 1f);
            sensitivitySlider.onValueChanged.RemoveAllListeners();
            sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        }

        SetButtonsInteractable(true);
        if (statusText != null) statusText.text = "Готов к созданию игры!";

        if (gameplayHUD != null) gameplayHUD.SetActive(false);
    }

    private async Task InitializeUnityServices()
    {
        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            isServicesInitialized = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Ошибка сервисов Unity: {e.Message}");
            if (statusText != null) statusText.text = "Ошибка авторизации сетей!";
            isServicesInitialized = false;
            SetButtonsInteractable(false);
        }
    }

    public async void CreateRelayGame()
    {
        if (!isServicesInitialized || isStarting || gameStarted) return;
        if (NetworkManager.Singleton == null) return;

        isStarting = true;
        SetButtonsInteractable(false);
        if (statusText != null) statusText.text = "Создаём сетевую комнату...";

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(4);
            currentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(new RelayServerData(allocation, "dtls"));

            TrySubscribe();
            NetworkManager.Singleton.StartHost();

            if (statusText != null) statusText.text = $"Код: {currentJoinCode}\nИгроков: 1/4";
            if (settingsJoinCodeText != null) settingsJoinCodeText.text = $"КОД КОМНАТЫ: {currentJoinCode}";

            gameStarted = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError(e);
            if (statusText != null) statusText.text = $"Ошибка Relay: {e.Message}";
            SetupUI();
        }
        isStarting = false;
    }

    public async void JoinRelayGame()
    {
        if (!isServicesInitialized || isStarting || gameStarted) return;
        if (NetworkManager.Singleton == null || joinCodeInput == null) return;

        string code = joinCodeInput.text.Trim().ToUpper();
        if (string.IsNullOrEmpty(code) || code.Length < 6)
        {
            if (statusText != null) statusText.text = "Код слишком короткий!";
            return;
        }

        isStarting = true;
        SetButtonsInteractable(false);
        if (statusText != null) statusText.text = $"Подключение к {code}...";

        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(code);
            currentJoinCode = code;

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(new RelayServerData(joinAllocation, "dtls"));

            TrySubscribe();
            NetworkManager.Singleton.StartClient();

            if (statusText != null) statusText.text = "Подключаемся к хостом...";
            if (settingsJoinCodeText != null) settingsJoinCodeText.text = $"КОД КОМНАТЫ: {currentJoinCode}";

            gameStarted = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError(e);
            if (statusText != null) statusText.text = $"Неверный код комнаты!";
            SetupUI();
        }
        isStarting = false;
    }

    public void LeaveGame()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
        gameStarted = false;
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (gameplayHUD != null) gameplayHUD.SetActive(false);
        SetupUI();
    }

    private void OnSensitivityChanged(float value)
    {
        PlayerPrefs.SetFloat("Sensitivity", value);
        PlayerPrefs.Save();
    }

    private void SetButtonsInteractable(bool state)
    {
        if (hostButton != null) hostButton.interactable = state;
        if (joinButton != null) joinButton.interactable = state;
    }

    private void SetUIActive(bool isActive)
    {
        if (menuPanel != null) menuPanel.SetActive(isActive);
    }

    private void SetGameplayControlsOnlyActive(bool isActive)
    {
        if (gameplayHUD == null || openSettingsButton == null) return;

        foreach (Transform child in gameplayHUD.transform)
        {
            if (child.gameObject != openSettingsButton.gameObject)
            {
                child.gameObject.SetActive(isActive);
            }
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            SetUIActive(false);
            if (gameplayHUD != null) gameplayHUD.SetActive(true);
            if (openSettingsButton != null) openSettingsButton.gameObject.SetActive(true);
            SetGameplayControlsOnlyActive(true);

            // Телепортация на SpawnPoint с правильным GetComponent<CharacterController>()
            GameObject spawnPoint = GameObject.Find("SpawnPoint");
            if (spawnPoint != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                CharacterController cc = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                NetworkManager.Singleton.LocalClient.PlayerObject.transform.position = spawnPoint.transform.position;
                if (cc != null) cc.enabled = true;
            }
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && statusText != null)
        {
            statusText.text = $"Код: {currentJoinCode}\nИгроков: {NetworkManager.Singleton.ConnectedClients.Count}/4";
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            LeaveGame();
        }
    }
}