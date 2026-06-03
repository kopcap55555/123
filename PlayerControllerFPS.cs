using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

[RequireComponent(typeof(CharacterController))]
public class PlayerControllerFPS : NetworkBehaviour
{
    [Header("Настройки движения")]
    public float walkSpeed = 5f; public float runSpeed = 8f; public float jumpForce = 7f; public float gravity = -9.81f;

    [Header("Чувствительность обзора")]
    public float mouseSensitivity = 2f; public float mobileCameraSensitivity = 0.3f;

    [Header("Здоровье (Мультиплеер)")]
    public NetworkVariable<int> currentHP = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private Slider healthSlider;

    [Header("Настройки стрельбы")]
    public float shootDistance = 20f; public int weaponDamage = 25;
    private Button shootButton;

    [Header("ДИНАМИЧЕСКИЙ СПАВН ОРУЖИЯ")]
    [Tooltip("Список префабов оружия из папки проекта (0 - Автомат, 1 - Пистолет)")]
    public GameObject[] weaponPrefabs;

    private NetworkVariable<int> equippedWeaponIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private Transform weaponHolder; private GameObject currentSpawnedWeaponModel;

    [Header("Ссылки")]
    public Camera playerCamera; private MobileJoystick movementJoystick; private MobileJumpButton jumpButton;

    private CharacterController controller; private Vector3 velocity; private float xRotation = 0f;
    private Vector2 moveInput, lookInput; private bool jumpRequested = false; private int cameraTouchId = -1;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (playerCamera == null) { playerCamera = GetComponentInChildren<Camera>(); }
        weaponHolder = transform.Find("Main Camera/WeaponHolder") ?? transform.GetComponentInChildren<Camera>()?.transform.Find("WeaponHolder");
    }

    public override void OnNetworkSpawn()
    {
        currentHP.OnValueChanged += OnHPChanged; equippedWeaponIndex.OnValueChanged += OnWeaponChanged;
        if (!IsOwner)
        {
            if (playerCamera != null)
            {
                playerCamera.gameObject.SetActive(false);
                if (playerCamera.GetComponent<AudioListener>()) { playerCamera.GetComponent<AudioListener>().enabled = false; }
            }
            if (controller != null) { controller.enabled = false; }
            UpdateWeaponModel(equippedWeaponIndex.Value); enabled = false; return;
        }
        var joysticks = Resources.FindObjectsOfTypeAll<MobileJoystick>();
        movementJoystick = joysticks.Length > 0 ? joysticks[0] : null;
        var jumpButtons = Resources.FindObjectsOfTypeAll<MobileJumpButton>();
        jumpButton = jumpButtons.Length > 0 ? jumpButtons[0] : null;
        FindGameplayUI(); UpdateWeaponModel(equippedWeaponIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        currentHP.OnValueChanged -= OnHPChanged; equippedWeaponIndex.OnValueChanged -= OnWeaponChanged;
    }

    public void ChangeWeapon(int weaponIndex)
    {
        if (!IsOwner) { return; }
        ChangeWeaponServerRpc(weaponIndex);
    }

    [ServerRpc] void ChangeWeaponServerRpc(int weaponIndex) { equippedWeaponIndex.Value = weaponIndex; }

    private void OnWeaponChanged(int previousIndex, int newIndex) { UpdateWeaponModel(newIndex); }

    void UpdateWeaponModel(int index)
    {
        if (currentSpawnedWeaponModel != null) { Destroy(currentSpawnedWeaponModel); currentSpawnedWeaponModel = null; }
        if (index < 0 || weaponPrefabs == null || index >= weaponPrefabs.Length) { return; }
        if (weaponPrefabs[index] != null && weaponHolder != null)
        {
            currentSpawnedWeaponModel = Instantiate(weaponPrefabs[index], weaponHolder.position, weaponHolder.rotation, weaponHolder);
            currentSpawnedWeaponModel.transform.localPosition = Vector3.zero; currentSpawnedWeaponModel.transform.localRotation = Quaternion.identity;
        }
    }

    void FindGameplayUI()
    {
        var sliders = Resources.FindObjectsOfTypeAll<Slider>();
        foreach (var slider in sliders)
        {
            if (slider.gameObject.name == "HealthSlider") { healthSlider = slider; healthSlider.value = currentHP.Value; break; }
        }
        var buttons = Resources.FindObjectsOfTypeAll<Button>();
        foreach (var btn in buttons)
        {
            if (btn.gameObject.name == "ShootButton") { shootButton = btn; shootButton.onClick.RemoveAllListeners(); shootButton.onClick.AddListener(ShootWeapon); break; }
        }
    }

    void Update()
    {
        if (movementJoystick == null) { movementJoystick = FindFirstObjectByType<MobileJoystick>(); }
        if (jumpButton == null) { jumpButton = FindFirstObjectByType<MobileJumpButton>(); }
        if ((healthSlider == null || shootButton == null) && IsOwner) { FindGameplayUI(); }
        GetInput(); HandleCamera();
        bool pcJump = Input.GetButtonDown("Jump"); bool mobileJump = jumpButton != null && jumpButton.JumpRequested;
        if (pcJump || mobileJump) { jumpRequested = true; if (mobileJump) { jumpButton.ResetJumpRequest(); } }
        if (Input.GetMouseButtonDown(1)) { ShootWeapon(); }
    }

    void FixedUpdate()
    {
        if (controller.isGrounded && velocity.y < 0) { velocity.y = -2f; }
        bool isRunning = Input.GetKey(KeyCode.LeftShift) || moveInput.magnitude > 0.9f; float speed = isRunning ? runSpeed : walkSpeed;
        Vector3 moveDirection = (transform.right * moveInput.x + transform.forward * moveInput.y).normalized * speed;
        if (jumpRequested) { if (controller.isGrounded) { velocity.y = Mathf.Sqrt(jumpForce * -2f * gravity); } jumpRequested = false; }
        velocity.y += gravity * Time.fixedDeltaTime; Vector3 finalMovement = moveDirection + velocity; controller.Move(finalMovement * Time.fixedDeltaTime);
    }

    void ShootWeapon()
    {
        if (equippedWeaponIndex.Value < 0) { Debug.Log("В руках нет оружия! Стрельба заблокирована."); return; }
        if (playerCamera == null) { return; }
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)); RaycastHit hit;
        int layerMask = ~LayerMask.GetMask("Ignore Raycast");
        if (Physics.Raycast(ray, out hit, shootDistance, layerMask))
        {
            NetworkObject netObj = hit.collider.GetComponent<NetworkObject>();
            if (netObj != null) { ShootTargetServerRpc(netObj.NetworkObjectId, weaponDamage); }
        }
    }

    [ServerRpc]
    void ShootTargetServerRpc(ulong targetNetObjectId, int damage)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(targetNetObjectId))
        {
            NetworkObject netObj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[targetNetObjectId];
            if (netObj != null)
            {
                EnemyAI enemy = netObj.GetComponent<EnemyAI>();
                if (enemy != null) { enemy.TakeDamage(damage); return; }
                FriendlyAI friendly = netObj.GetComponent<FriendlyAI>();
                if (friendly != null) { friendly.TakeDamage(damage); return; }
                PlayerControllerFPS otherPlayer = netObj.GetComponent<PlayerControllerFPS>();
                if (otherPlayer != null && otherPlayer != this) { otherPlayer.TakeDamage(damage); }
            }
        }
    }

    public void TakeDamage(int damageAmount)
    {
        if (!IsServer) { return; }
        currentHP.Value -= damageAmount;
        if (currentHP.Value <= 0) { currentHP.Value = 0; PlayerDeath(); }
    }

    void PlayerDeath()
    {
        currentHP.Value = 100; GameObject spawnPoint = GameObject.Find("SpawnPoint");
        if (spawnPoint != null)
        {
            if (controller != null) { controller.enabled = false; }
            transform.position = spawnPoint.transform.position;
            if (controller != null) { controller.enabled = true; }
        }
    }

    private void OnHPChanged(int previousValue, int newValue)
    {
        if (IsOwner && healthSlider != null) { healthSlider.value = newValue; }
    }

    public void BuildBlock(Vector3 spawnPos, int itemIndex)
    {
        if (!IsOwner) { return; }
        SpawnBlockServerRpc(spawnPos, itemIndex);
    }

    [ServerRpc]
    void SpawnBlockServerRpc(Vector3 spawnPos, int itemIndex)
    {
        if (BuildManager.Instance == null || itemIndex < 0 || itemIndex >= BuildManager.Instance.allItems.Length) { return; }
        GameObject realPrefab = BuildManager.Instance.allItems[itemIndex].solidPrefab;
        if (realPrefab != null && realPrefab.GetComponent<EnemyAI>() != null)
        {
            if (ServerMatchController.Instance != null && !ServerMatchController.Instance.CanSpawnZombie()) { return; }
        }
        if (realPrefab != null)
        {
            GameObject newBlock = Instantiate(realPrefab, spawnPos, Quaternion.identity); newBlock.GetComponent<NetworkObject>().Spawn(true);
        }
    }

    public void DestroyBlock(ulong networkObjectId)
    {
        if (!IsOwner) { return; }
        DestroyBlockServerRpc(networkObjectId);
    }

    [ServerRpc]
    void DestroyBlockServerRpc(ulong networkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(networkObjectId))
        {
            NetworkObject netObj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[networkObjectId];
            if (netObj != null) { netObj.Despawn(true); }
        }
    }

    void GetInput()
    {
        moveInput = Vector2.zero;
        if (movementJoystick != null) { moveInput.x = movementJoystick.Horizontal; moveInput.y = movementJoystick.Vertical; }
        if (moveInput.x == 0 && moveInput.y == 0) { moveInput.x = Input.GetAxisRaw("Horizontal"); moveInput.y = Input.GetAxisRaw("Vertical"); }
        lookInput = Vector2.zero;
        if (Input.GetMouseButton(0)) { lookInput.x = Input.GetAxis("Mouse X") * mouseSensitivity; lookInput.y = Input.GetAxis("Mouse Y") * mouseSensitivity; }
        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                {
                    if (touch.position.x > Screen.width * 0.5f && cameraTouchId == -1) { cameraTouchId = touch.fingerId; }
                }
                if (touch.fingerId == cameraTouchId)
                {
                    if (touch.phase == TouchPhase.Moved)
                    {
                        lookInput.x = touch.deltaPosition.x * mobileCameraSensitivity;
                        lookInput.y = touch.deltaPosition.y * mobileCameraSensitivity;
                    }
                    if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        cameraTouchId = -1;
                    }
                }
            }
        }
    }

    void HandleCamera() { if (playerCamera == null) { return; } float sensMultiplier = PlayerPrefs.GetFloat("Sensitivity", 1f); float finalLookX = lookInput.x * sensMultiplier; float finalLookY = lookInput.y * sensMultiplier; transform.Rotate(Vector3.up * finalLookX); xRotation = Mathf.Clamp(xRotation - finalLookY, -90f, 90f); playerCamera.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f); }
}
