using UnityEngine;
using Unity.Netcode;

public class BuildManager : MonoBehaviour
{
    private static BuildManager _instance;
    public static BuildManager Instance
    {
        get
        {
            if (_instance == null) _instance = FindFirstObjectByType<BuildManager>();
            return _instance;
        }
    }

    [Header("База данных построек")]
    public BuildItemData[] allItems;

    [Header("Настройки")]
    public float maxBuildDistance = 5f;
    public LayerMask buildLayerMask;

    private BuildItemData currentItem;
    private GameObject activeGhost;
    private Transform playerCameraTransform;
    private Vector3 targetSpawnPosition;
    private bool isGhostActive = false;

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    public void SelectItemToBuild(int index)
    {
        if (allItems == null || index < 0 || index >= allItems.Length) return;

        CancelGhost();
        currentItem = allItems[index];

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            playerCameraTransform = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponentInChildren<Camera>().transform;
        }

        if (currentItem != null && currentItem.ghostPrefab != null && playerCameraTransform != null)
        {
            activeGhost = Instantiate(currentItem.ghostPrefab);
            isGhostActive = true;
            if (BuildMenuUI.Instance != null) BuildMenuUI.Instance.OnGhostActivated();
        }
    }

    public void StartDestroyMode()
    {
        CancelGhost();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            playerCameraTransform = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponentInChildren<Camera>().transform;
        }

        if (playerCameraTransform == null) return;

        Camera mainCam = playerCameraTransform.GetComponent<Camera>();
        if (mainCam == null) mainCam = Camera.main;

        Ray ray = mainCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, maxBuildDistance, buildLayerMask))
        {
            NetworkObject netObj = hit.collider.GetComponent<NetworkObject>();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                PlayerControllerFPS localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerControllerFPS>();
                if (netObj != null && localPlayer != null && !hit.collider.GetComponent<PlayerControllerFPS>())
                {
                    localPlayer.DestroyBlock(netObj.NetworkObjectId);
                }
            }
        }
        if (BuildMenuUI.Instance != null) BuildMenuUI.Instance.ClickedCancel();
    }

    void Update()
    {
        if (!isGhostActive || playerCameraTransform == null || activeGhost == null) return;
        UpdateGhostPosition();
    }

    void UpdateGhostPosition()
    {
        Camera mainCam = playerCameraTransform.GetComponent<Camera>();
        if (mainCam == null) mainCam = Camera.main;

        Ray ray = mainCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit hit;
        Vector3 smoothTargetPos;

        if (Physics.Raycast(ray, out hit, maxBuildDistance, buildLayerMask))
        {
            int gridX = Mathf.RoundToInt(hit.point.x + hit.normal.x * 0.5f);
            int gridZ = Mathf.RoundToInt(hit.point.z + hit.normal.z * 0.5f);
            int gridY = Mathf.Abs(hit.normal.y) > 0.5f ? (hit.normal.y > 0 ? Mathf.CeilToInt(hit.point.y) : Mathf.FloorToInt(hit.point.y) - 1) : Mathf.RoundToInt(hit.point.y + hit.normal.y * 0.5f);

            targetSpawnPosition = new Vector3(gridX, gridY + 0.5f, gridZ);
            smoothTargetPos = hit.point + hit.normal * 0.5f;
            if (!activeGhost.activeSelf) activeGhost.SetActive(true);
        }
        else
        {
            // ИСПРАВЛЕНО: Теперь берется правильное направление луча ray.direction
            Vector3 rawTarget = ray.origin + ray.direction * maxBuildDistance;
            targetSpawnPosition = new Vector3(Mathf.RoundToInt(rawTarget.x), Mathf.FloorToInt(rawTarget.y) + 0.5f, Mathf.RoundToInt(rawTarget.z));
            smoothTargetPos = targetSpawnPosition;
            if (!activeGhost.activeSelf) activeGhost.SetActive(true);
        }

        float smoothingSpeed = 25f;
        float t = 1f - Mathf.Exp(-smoothingSpeed * Time.deltaTime);
        activeGhost.transform.position = Vector3.Lerp(activeGhost.transform.position, smoothTargetPos, t);
        activeGhost.transform.rotation = Quaternion.identity;
    }

    public void ConfirmAction()
    {
        if (!isGhostActive || currentItem == null || activeGhost == null || !activeGhost.activeSelf) return;
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient.PlayerObject == null) return;

        PlayerControllerFPS localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerControllerFPS>();
        if (localPlayer != null)
        {
            int itemIndex = System.Array.IndexOf(allItems, currentItem);
            localPlayer.BuildBlock(targetSpawnPosition, itemIndex);
        }
    }

    public void CancelGhost()
    {
        if (activeGhost != null) Destroy(activeGhost);
        isGhostActive = false;
        currentItem = null;
    }
}
