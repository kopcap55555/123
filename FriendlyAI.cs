using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class FriendlyAI : NetworkBehaviour
{
    [Header("НАСТРОЙКИ МИРНОГО ЖИТЕЛЯ")]
    [SerializeField, InspectorName("Скорость прогулки")]
    private float walkSpeed = 2f;
    [SerializeField, InspectorName("Скорость бега (Паника)")]
    private float panicSpeed = 5f;
    [SerializeField, InspectorName("Радиус прогулки")]
    private float walkRadius = 8f;
    [SerializeField, InspectorName("Радиус страха (Зомби рядом)")]
    private float fearRadius = 6f;

    [Header("ЗДОРОВЬЕ ЖИТЕЛЯ")]
    [SerializeField, InspectorName("Максимальное ХП")]
    private int maxHP = 40;
    private int currentHP;

    private NetworkVariable<int> skinIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Vector3 targetPosition;
    private Transform dangerZombie;
    private bool isPanicking = false;
    private bool isWalkingToPoint = false;
    private bool isDead = false;
    private CapsuleCollider capsuleCollider;

    void Awake()
    {
        capsuleCollider = GetComponent<CapsuleCollider>();
        if (capsuleCollider == null) capsuleCollider = gameObject.AddComponent<CapsuleCollider>();
    }

    void Start()
    {
        currentHP = maxHP;

        if (IsServer)
        {
            InvokeRepeating(nameof(CheckDangerOptimized), 1f, 0.33f);
            ChooseNewWalkPoint();
            skinIndex.Value = 1; // Индекс карточки жителя
        }

        ApplyCustomSkin(skinIndex.Value);
    }

    public void ApplyCustomSkin(int itemIndex)
    {
        if (itemIndex < 0 || BuildManager.Instance == null || BuildManager.Instance.allItems == null) return;
        if (itemIndex >= BuildManager.Instance.allItems.Length) return;

        BuildItemData data = BuildManager.Instance.allItems[itemIndex];
        if (data == null || data.customSkinPrefab == null) return;

        MeshRenderer mainRenderer = GetComponent<MeshRenderer>();
        if (mainRenderer != null) mainRenderer.enabled = false;

        foreach (Transform child in transform)
        {
            if (child.name == "CustomSkin_Generated") Destroy(child.gameObject);
        }

        GameObject newSkin = Instantiate(data.customSkinPrefab, transform.position, transform.rotation, transform);
        newSkin.name = "CustomSkin_Generated";

        Bounds combinedBounds = new Bounds(newSkin.transform.position, Vector3.zero);
        Renderer[] renderers = newSkin.GetComponentsInChildren<Renderer>();

        if (renderers.Length > 0)
        {
            foreach (Renderer rend in renderers)
            {
                combinedBounds.Encapsulate(rend.bounds);
            }

            capsuleCollider.height = combinedBounds.size.y;
            capsuleCollider.radius = Mathf.Max(combinedBounds.size.x, combinedBounds.size.z) * 0.5f;
            capsuleCollider.center = new Vector3(0, capsuleCollider.height * 0.5f, 0);
        }
    }

    void Update()
    {
        if (!IsServer || isDead) return;

        if (isPanicking && dangerZombie != null)
        {
            Vector3 fleeDirection = (transform.position - dangerZombie.position).normalized;
            fleeDirection.y = 0;

            transform.position += fleeDirection * panicSpeed * Time.deltaTime;

            if (fleeDirection != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(fleeDirection);
            }
            isWalkingToPoint = false;
        }
        else
        {
            float distanceToTarget = Vector3.Distance(transform.position, targetPosition);

            if (distanceToTarget > 0.5f)
            {
                Vector3 moveDirection = (targetPosition - transform.position).normalized;
                moveDirection.y = 0;

                transform.position += moveDirection * walkSpeed * Time.deltaTime;

                if (moveDirection != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(moveDirection);
                }
            }
            else if (!isWalkingToPoint)
            {
                StartCoroutine(RestTimerCoroutine());
            }
        }
    }

    public void TakeDamage(int damageAmount)
    {
        if (!IsServer || isDead) return;
        currentHP -= damageAmount;

        if (currentHP <= 0)
        {
            InfectIntoZombie();
        }
    }

    void InfectIntoZombie()
    {
        isDead = true;
        CancelInvoke();

        if (BuildManager.Instance != null && BuildManager.Instance.allItems != null && BuildManager.Instance.allItems.Length > 2)
        {
            GameObject zombiePrefab = BuildManager.Instance.allItems[2].solidPrefab;

            if (zombiePrefab != null)
            {
                Vector3 spawnPos = transform.position;
                GameObject newZombie = Instantiate(zombiePrefab, spawnPos, Quaternion.identity);
                newZombie.GetComponent<NetworkObject>().Spawn();
            }
        }

        GetComponent<NetworkObject>().Despawn(true);
    }

    void CheckDangerOptimized()
    {
        if (isDead) return;

        EnemyAI[] allZombies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        float closestDistance = fearRadius;
        Transform foundEnemy = null;

        foreach (EnemyAI zombie in allZombies)
        {
            if (zombie == null) continue;
            float dist = Vector3.Distance(transform.position, zombie.transform.position);

            if (dist < closestDistance)
            {
                closestDistance = dist;
                foundEnemy = zombie.transform;
            }
        }

        if (foundEnemy != null)
        {
            dangerZombie = foundEnemy;
            isPanicking = true;
        }
        else
        {
            if (isPanicking)
            {
                isPanicking = false;
                dangerZombie = null;
                ChooseNewWalkPoint();
            }
        }
    }

    void ChooseNewWalkPoint()
    {
        float randomX = Random.Range(-walkRadius, walkRadius);
        float randomZ = Random.Range(-walkRadius, walkRadius);

        targetPosition = new Vector3(transform.position.x + randomX, transform.position.y, transform.position.z + randomZ);
        isWalkingToPoint = true;
    }

    IEnumerator RestTimerCoroutine()
    {
        isWalkingToPoint = true;
        yield return new WaitForSeconds(Random.Range(2f, 4f));

        if (!isPanicking && !isDead)
        {
            ChooseNewWalkPoint();
        }
    }

    public override void OnNetworkDespawn()
    {
        CancelInvoke();
    }
}
