using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BuildMenuUI : MonoBehaviour
{
    public static BuildMenuUI Instance;

    [Header("Ссылки на окна")]
    public GameObject menuPanel;          // Панель BuildMenuPanel
    public GameObject openMenuButton;     // Кнопка ИНВЕНТАРЬ (Стройка)
    public GameObject cancelBuildButton;  // Кнопка Крестик отмены

    [Header("Контенты трех вкладок")]
    public GameObject contentBlocks;      // Объект Content_Blocks
    public GameObject contentCharacters;  // Объект Content_Characters
    public GameObject contentWeapons;     // Объект Content_Weapons

    [Header("Кнопка действия на экране")]
    public Button confirmActionButton;    // Кнопка ПОСТРОИТЬ
    private TextMeshProUGUI confirmText;
    private Image confirmImage;

    private bool isMenuOpen = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        if (confirmActionButton != null)
        {
            confirmText = confirmActionButton.GetComponentInChildren<TextMeshProUGUI>();
            confirmImage = confirmActionButton.GetComponent<Image>();
        }
    }

    void Start()
    {
        if (menuPanel != null) menuPanel.SetActive(false);
        if (confirmActionButton != null) confirmActionButton.gameObject.SetActive(false);
        if (cancelBuildButton != null) cancelBuildButton.SetActive(false);
        if (openMenuButton != null) openMenuButton.SetActive(true);

        if (openMenuButton != null) openMenuButton.GetComponent<Button>().onClick.AddListener(ToggleMenuFromButton);
        if (confirmActionButton != null) confirmActionButton.onClick.AddListener(ClickedConfirmAction);
    }

    public void ToggleMenuFromButton()
    {
        isMenuOpen = !isMenuOpen;
        if (menuPanel != null) menuPanel.SetActive(isMenuOpen);

        if (isMenuOpen)
        {
            if (BuildManager.Instance != null) BuildManager.Instance.CancelGhost();
            if (confirmActionButton != null) confirmActionButton.gameObject.SetActive(false);
            ShowTab(0);
        }
    }

    public void ShowTab(int tabIndex)
    {
        if (contentBlocks != null) contentBlocks.SetActive(tabIndex == 0);
        if (contentCharacters != null) contentCharacters.SetActive(tabIndex == 1);
        if (contentWeapons != null) contentWeapons.SetActive(tabIndex == 2);
    }

    public void ClickedWeaponIcon(int weaponIndex)
    {
        if (Unity.Netcode.NetworkManager.Singleton == null) return;
        var localPlayerObj = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject;

        if (localPlayerObj != null)
        {
            PlayerControllerFPS localPlayer = localPlayerObj.GetComponent<PlayerControllerFPS>();
            if (localPlayer != null)
            {
                localPlayer.ChangeWeapon(weaponIndex);
                ToggleMenuFromButton();
            }
        }
    }

    public void ClickedHideWeapon()
    {
        ClickedWeaponIcon(-1);
    }

    public void ClickedOpenDestroy()
    {
        if (BuildManager.Instance != null) BuildManager.Instance.StartDestroyMode();
    }

    public void OnGhostActivated()
    {
        if (menuPanel != null) menuPanel.SetActive(false);
        if (openMenuButton != null) openMenuButton.SetActive(false);
        if (cancelBuildButton != null) cancelBuildButton.SetActive(true);

        if (confirmActionButton != null)
        {
            confirmActionButton.gameObject.SetActive(true);
            if (confirmText != null) confirmText.text = "ПОСТРОИТЬ";
            if (confirmImage != null) confirmImage.color = new Color(0.2f, 0.8f, 0.2f, 1f);
        }
        isMenuOpen = false;
    }

    public void ClickedItemIcon(int index)
    {
        if (BuildManager.Instance != null)
        {
            BuildManager.Instance.SelectItemToBuild(index);
        }
    }

    public void ClickedConfirmAction()
    {
        if (BuildManager.Instance != null) BuildManager.Instance.ConfirmAction();
    }

    public void ClickedCancel()
    {
        // 1. Отменяем зеленого призрака, если он был активен
        if (BuildManager.Instance != null)
            BuildManager.Instance.CancelGhost();

        // 2. Выключаем кнопку «Построить» и кнопку «Крестик» (Х) на экране
        if (confirmActionButton != null) confirmActionButton.gameObject.SetActive(false);
        if (cancelBuildButton != null) cancelBuildButton.SetActive(false);

        // 3. Включаем обратно главную кнопку «ИНВЕНТАРЬ» на основном HUD экрана
        if (openMenuButton != null) openMenuButton.SetActive(true);

        // 4. ЖЕСТКО ВЫКЛЮЧАЕМ всю большую панель инвентаря, чтобы она исчезла с экрана!
        if (menuPanel != null) menuPanel.SetActive(false);
        isMenuOpen = false;
    }

}
