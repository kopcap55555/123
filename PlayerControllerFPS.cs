using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

[RequireComponent(typeof(CharacterController))]
public class PlayerControllerFPS : NetworkBehaviour
{
    [Header("Настройки движения")]
    public float walkSpeed = 5f;
    public float runSpeed = 8f;
    public float jumpForce = 7f;
    public float gravity = -9.81f;

    [Header("Чувствительность обзора")]
    public float mouseSensitivity = 2f;
    public float mobileCameraSensitivity = 0.3f;

    [Header("Здоровье (Мультиплеер)")]
    public NetworkVariable<int> currentHP = new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private Slider healthSlider;

    [Header("Физическая стрельба")]
    public GameObject bulletPrefab; // Префаб пули с компонентом NetProjectile
    public float bulletSpeed = 40f; // Скорость полета патрона
    public float fireRate = 0.2f;   // Задержка между выстрелами (автоматический огонь)
    public int weaponDamage = 25;   // Урон от пули
    private float nextFireTime = 0f;

    [Header("ДИНАМИЧЕСКИЙ СПАВН ОРУЖИЯ")]
    [Tooltip("Список префабов оружия из папки проекта (0 - Автомат, 1 - Пистолет)")]
    public GameObject[] weaponPrefabs;

    private NetworkVariable<int> equippedWeaponIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private Transform weaponHolder;
    private GameObject currentSpawnedWeaponModel;

    [Header("Ссылки")]
    public Camera playerCamera;
    private MobileJoystick movementJoystick;
    private MobileJumpButton jumpButton;
    private MobileShootButton shootButtonScript;

    private CharacterController controller;
    private Vector3 velocity;
    private float xRotation = 0f;

    private Vector2 moveInput;
    private Vector2 lookInput;
    private bool jumpRequested = false;
    private int cameraTouchId = -1;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();

        weaponHolder = transform.Find("Main Camera/WeaponHolder") ?? transform.GetComponentInChildren<Camera>()?.transform.Find("WeaponHolder");
    }

    public override void OnNetworkSpawn()
    {
        currentHP.OnValueChanged += OnHPChanged;
        equippedWeaponIndex.OnValueChanged += OnWeaponChanged;

        if (!IsOwner)
        {
            if (playerCamera != null)
            {
                playerCamera.gameObject.SetActive(false);
                if (playerCamera.GetComponent<AudioListener>())
                    playerCamera.GetComponent<AudioListener>().enabled = false;
            }
            if (controller != null) controller.enabled = false;

            UpdateWeaponModel(equippedWeaponIndex.Value);
            enabled = false;
            return;
        }

        // Поиск мобильных элементов управления на Canvas
        movementJoystick = FindFirstObjectByType<MobileJoystick>();
        jumpButton = FindFirstObjectByType<MobileJumpButton>();
        shootButtonScript = FindFirstObjectByType<MobileShootButton>();

        FindGameplayUI();
        UpdateWeaponModel(equippedWeaponIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        currentHP.OnValueChanged -= OnHPChanged;
        equippedWeaponIndex.OnValueChanged -= OnWeaponChanged;
    }

    public void ChangeWeapon(int weaponIndex)
    {
        if (!IsOwner) return;
        ChangeWeaponServerRpc(weaponIndex);
    }

    [ServerRpc]
    void ChangeWeaponServerRpc(int weaponIndex)
    {
        equippedWeaponIndex.Value = weaponIndex;
    }

    private void OnWeaponChanged(int previousIndex, int newIndex)
    {
        UpdateWeaponModel(newIndex);
    }

    void UpdateWeaponModel(int index)
    {
        if (currentSpawnedWeaponModel != null)
        {
            Destroy(currentSpawnedWeaponModel);
            currentSpawnedWeaponModel = null;
        }

        if (index < 0 || weaponPrefabs == null || index >= weaponPrefabs.Length) return;

        if (weaponPrefabs[index] != null && weaponHolder != null)
        {
            currentSpawnedWeaponModel = Instantiate(weaponPrefabs[index], weaponHolder.position, weaponHolder.rotation, weaponHolder);
            currentSpawnedWeaponModel.transform.localPosition = Vector3.zero;
            currentSpawnedWeaponModel.transform.localRotation = Quaternion.identity;
        }
    }

    void FindGameplayUI()
    {
        var sliders = Resources.FindObjectsOfTypeAll<Slider>();
        foreach (var slider in sliders)
        {
            if (slider.gameObject.name == "HealthSlider")
            {
                healthSlider = slider;
                healthSlider.value = currentHP.Value;
                break;
            }
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        // Постоянная подстраховка ссылок на UI элементы
        if (movementJoystick == null) movementJoystick = FindFirstObjectByType<MobileJoystick>();
        if (jumpButton == null) jumpButton = FindFirstObjectByType<MobileJumpButton>();
        if (shootButtonScript == null) shootButtonScript = FindFirstObjectByType<MobileShootButton>();
        if (healthSlider == null) FindGameplayUI();

        GetInput();
        HandleCamera();

        // Проверка прыжка
        bool pcJump = Input.GetButtonDown("Jump");
        bool mobileJump = jumpButton != null && jumpButton.JumpRequested;

        if (pcJump || mobileJump)
        {
            jumpRequested = true;
            if (mobileJump) jumpButton.ResetJumpRequest();
        }

        // Автоматическая стрельба по зажатию (ПК или Мобилка)
        bool pcShoot = Input.GetMouseButton(0); 
        bool mobileShoot = shootButtonScript != null && shootButtonScript.IsHoldingShoot;

        if ((pcShoot || mobileShoot) && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + fireRate;
            ShootWeapon();
        }
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;

        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        bool isRunning = Input.GetKey(KeyCode.LeftShift) || moveInput.magnitude > 0.9f;
        float speed = isRunning ? runSpeed : walkSpeed;
        Vector3 moveDirection = (transform.right * moveInput.x + transform.forward * moveInput.y).normalized * speed;

        if (jumpRequested)
        {
            if (controller.isGrounded)
            {
                velocity.y = Mathf.Sqrt(jumpForce * -2f * gravity);
            }
            jumpRequested = false;
        }

        velocity.y += gravity * Time.fixedDeltaTime;
        Vector3 finalMovement = moveDirection + velocity;
        controller.Move(finalMovement * Time.fixedDeltaTime);
    }

    void ShootWeapon()
    {
        if (equippedWeaponIndex.Value < 0)
        {
            Debug.Log("В руках нет оружия! Стрельба заблокирована.");
            return;
        }

        if (playerCamera == null) return;

        // Точка и направление вылета физического патрона
        Vector3 spawnPos = playerCamera.transform.position + playerCamera.transform.forward * 1.2f;
        Quaternion spawnRot = playerCamera.transform.rotation;

        SpawnBulletServerRpc(spawnPos, spawnRot);
    }

    [ServerRpc]
    void SpawnBulletServerRpc(Vector3 position, Quaternion rotation)
    {
        if (bulletPrefab == null) return;

        // Спавн физического объекта пули на сервере
        GameObject bullet = Instantiate(bulletPrefab, position, rotation);
        
        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = bullet.transform.forward * bulletSpeed;
        }

        NetProjectile projectileScript = bullet.GetComponent<NetProjectile>();
        if (projectileScript != null)
        {
            projectileScript.Initialize(weaponDamage, OwnerClientId);
        }

        // Регистрируем объект пули в сети Netcode
        bullet.GetComponent<NetworkObject>().Spawn(true);
        
        // Транслируем звук выстрела всем клиентам
        PlayShootSoundClientRpc();
    }

    [ClientRpc]
    void PlayShootSoundClientRpc()
    {
        if (currentSpawnedWeaponModel != null)
        {
            AudioSource source = currentSpawnedWeaponModel.GetComponent<AudioSource>();
            if (source != null && source.clip != null)
            {
                source.PlayOneShot(source.clip);
            }
        }
    }

    public void TakeDamage(int damageAmount)
    {
        if (!IsServer) return;

        currentHP.Value -= damageAmount;

        if (currentHP.Value <= 0)
        {
            currentHP.Value = 0;
            PlayerDeath();
        }
    }

    void PlayerDeath()
    {
        currentHP.Value = 100;
        GameObject spawnPoint = GameObject.Find("SpawnPoint");
        if (spawnPoint != null)
        {
            RespawnPlayerClientRpc(spawnPoint.transform.position);
        }
    }

    [ClientRpc]
    void RespawnPlayerClientRpc(Vector3 spawnPosition)
    {
        if (IsOwner)
        {
            if (controller != null) controller.enabled = false;
            transform.position = spawnPosition;
            if (controller != null) controller.enabled = true;
        }
    }

    private void OnHPChanged(int previousValue, int newValue)
    {
        if (IsOwner && healthSlider != null)
        {
            healthSlider.value = newValue;
        }
    }

    public void BuildBlock(Vector3 spawnPos, int itemIndex)
    {
        if (!IsOwner) return;
        SpawnBlockServerRpc(spawnPos, itemIndex);
    }

    [ServerRpc]
    void SpawnBlockServerRpc(Vector3 spawnPos, int itemIndex)
    {
        if (BuildManager.Instance == null || itemIndex < 0 || itemIndex >= BuildManager.Instance.allItems.Length) return;

        GameObject realPrefab = BuildManager.Instance.allItems[itemIndex].solidPrefab;

Используйте код с осторожностью.if (realPrefab != null && realPrefab.GetComponent() != null){if (ServerMatchController.Instance != null && !ServerMatchController.Instance.CanSpawnZombie()){return;}}if (realPrefab != null){GameObject newBlock = Instantiate(realPrefab, spawnPos, Quaternion.identity);newBlock.GetComponent().Spawn(true);}}public void DestroyBlock(ulong networkObjectId){if (!IsOwner) return;DestroyBlockServerRpc(networkObjectId);}[ServerRpc]void DestroyBlockServerRpc(ulong networkObjectId){if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj)){if (netObj != null) netObj.Despawn(true);}}void GetInput(){moveInput = Vector2.zero;if (movementJoystick != null){moveInput.x = movementJoystick.Horizontal;moveInput.y = movementJoystick.Vertical;}if (moveInput.x == 0 && moveInput.y == 0){moveInput.x = Input.GetAxisRaw("Horizontal");moveInput.y = Input.GetAxisRaw("Vertical");}lookInput = Vector2.zero;// Обзор мышкой на ПК по удержанию ЛКМ (в отсутствие тачей)if (Input.GetMouseButton(0) && Input.touchCount == 0){lookInput.x = Input.GetAxis("Mouse X") * mouseSensitivity;lookInput.y = Input.GetAxis("Mouse Y") * mouseSensitivity;}// Мобильный тачпад (Правая половина экрана)if (Input.touchCount > 0){for (int i = 0; i < Input.touchCount; i++){Touch touch = Input.GetTouch(i);if (touch.phase == TouchPhase.Began){if (touch.position.x > Screen.width * 0.5f && cameraTouchId == -1){cameraTouchId = touch.fingerId;}}if (touch.fingerId == cameraTouchId){if (touch.phase == TouchPhase.Moved){lookInput.x = touch.deltaPosition.x * mobileCameraSensitivity;lookInput.y = touch.deltaPosition.y * mobileCameraSensitivity;}if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled){cameraTouchId = -1;}}}}}void HandleCamera(){if (playerCamera == null) return;float sensMultiplier = PlayerPrefs.GetFloat("Sensitivity", 1f);float finalLookX = lookInput.x * sensMultiplier;float finalLookY = lookInput.y * sensMultiplier;transform.Rotate(Vector3.up * finalLookX);xRotation = Mathf.Clamp(xRotation - finalLookY, -90f, 90f);playerCamera.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);}}
