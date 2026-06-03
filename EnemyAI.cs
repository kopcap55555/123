using UnityEngine;
using Unity.Netcode;

public class EnemyAI : NetworkBehaviour
{
    [Header("НАСТРОЙКИ ЗОМБИ")]
    [SerializeField, InspectorName("Скорость бега")]
    private float moveSpeed = 3.5f;
    [SerializeField, InspectorName("Радиус удара лапой")]
    private float attackRange = 1.8f;
    [SerializeField, InspectorName("Радиус обнаружения цели")]
    private float chaseRange = 15f;
    [SerializeField, InspectorName("Урон за один укус")]
    private int attackDamage = 20;
    [SerializeField, InspectorName("Перезарядка удара (сек)")]
    private float attackCooldown = 1.0f;

    [Header("ЗДОРОВЬЕ ЗОМБИ")]
    [SerializeField, InspectorName("Максимальное ХП")]
    private int maxHP = 100;
    private int currentHP;

    private NetworkVariable<int> skinIndex = new NetworkVariable<int>(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Transform target;
    private PlayerControllerFPS targetPlayerScript;
    private FriendlyAI targetFriendlyScript;
    private float nextAttackTime = 0f;
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
            InvokeRepeating(nameof(FindNearestTargetOptimized), 0.5f, 0.33f);
            skinIndex.Value = 2; // Индекс карточки зомби
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
        if (!IsServer || target == null) return;

        float distance = Vector3.Distance(transform.position, target.position);

        if (distance > attackRange)
        {
            Vector3 direction = (target.position - transform.position).normalized;
            direction.y = 0;

            transform.position += direction * moveSpeed * Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(direction);
        }
        else
        {
            if (Time.time >= nextAttackTime)
            {
                AttackTarget();
                nextAttackTime = Time.time + attackCooldown;
            }
        }
    }

    void FindNearestTargetOptimized()
    {
        float closestDistance = chaseRange;
        Transform newTarget = null;
        targetPlayerScript = null;
        targetFriendlyScript = null;

        PlayerControllerFPS[] players = FindObjectsByType<PlayerControllerFPS>(FindObjectsSortMode.None);
        foreach (PlayerControllerFPS p in players)
        {
            float dist = Vector3.Distance(transform.position, p.transform.position);
            if (dist < closestDistance)
            {
                closestDistance = dist;
                newTarget = p.transform;
                targetPlayerScript = p;
            }
        }

        FriendlyAI[] friendlies = FindObjectsByType<FriendlyAI>(FindObjectsSortMode.None);
        foreach (FriendlyAI f in friendlies)
        {
            float dist = Vector3.Distance(transform.position, f.transform.position);
            if (dist < closestDistance)
            {
                closestDistance = dist;
                newTarget = f.transform;
                targetFriendlyScript = f;
                targetPlayerScript = null;
            }
        }

        target = newTarget;
    }

    void AttackTarget()
    {
        if (targetPlayerScript != null) targetPlayerScript.TakeDamage(attackDamage);
        else if (targetFriendlyScript != null) targetFriendlyScript.TakeDamage(attackDamage);
    }

    public void TakeDamage(int damage)
    {
        if (!IsServer) return;
        currentHP -= damage;

        if (currentHP <= 0)
        {
            CancelInvoke();
            GetComponent<NetworkObject>().Despawn(true);
        }
    }
}
