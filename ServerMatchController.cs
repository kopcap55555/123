using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class ServerMatchController : NetworkBehaviour
{
    public static ServerMatchController Instance;

    [Header("НАСТРОЙКИ СЕРВЕРА")]
    [SerializeField, InspectorName("Ограничение ФПС")]
    private int serverTargetFPS = 90;

    [Header("ЛИМИТЫ ПЕРСОНАЖЕЙ")]
    [SerializeField, InspectorName("Лимит Зомби")]
    private int maxAllowedZombies = 30;

    [SerializeField, InspectorName("Интервал Очистки Памяти")]
    private float garbageCollectInterval = 60f;

    [Header("ЗАЩИТА ОТ ПАДЕНИЯ ПОД КАРТУ")]
    [SerializeField, InspectorName("Высота Бездны (Y)")]
    private float voidKillHeight = -20f;

    [SerializeField, InspectorName("Интервал Проверки Бездны")]
    private float voidCheckInterval = 0.5f;

    private Coroutine voidCheckCoroutine;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            enabled = false;
            return;
        }

        Application.targetFrameRate = serverTargetFPS;
        Debug.Log($"<b>[СЕРВЕР]</b> Лимит FPS зафиксирован на отметке: {serverTargetFPS}");

        StartCoroutine(ServerGarbageCollectorCoroutine());

        if (voidCheckCoroutine != null) StopCoroutine(voidCheckCoroutine);
        voidCheckCoroutine = StartCoroutine(VoidCheckCoroutine());
    }

    private IEnumerator VoidCheckCoroutine()
    {
        while (IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            ProtectWorldFromVoid();
            yield return new WaitForSeconds(voidCheckInterval);
        }
    }

    private void ProtectWorldFromVoid()
    {
        PlayerControllerFPS[] allPlayers = FindObjectsByType<PlayerControllerFPS>(FindObjectsSortMode.None);
        foreach (PlayerControllerFPS player in allPlayers)
        {
            if (player != null && player.transform.position.y < voidKillHeight)
            {
                Debug.LogWarning($"<b>[СЕРВЕР]</b> Игрок {player.NetworkObjectId} упал в бездну!");
                player.TakeDamage(999999);
            }
        }

        EnemyAI[] allZombies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        foreach (EnemyAI zombie in allZombies)
        {
            if (zombie != null && zombie.transform.position.y < voidKillHeight)
            {
                Debug.LogWarning($"<b>[СЕРВЕР]</b> Зомби {zombie.NetworkObjectId} провалился под текстуры!");
                zombie.TakeDamage(999999);
            }
        }
    }

    public bool CanSpawnZombie()
    {
        if (!IsServer) return false;

        EnemyAI[] currentZombies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        if (currentZombies.Length >= maxAllowedZombies)
        {
            Debug.LogWarning($"<b>[СЕРВЕР]</b> Спавн заблокирован! Достигнут лимит зомби.");
            return false;
        }

        return true;
    }

    private IEnumerator ServerGarbageCollectorCoroutine()
    {
        while (IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            yield return new WaitForSeconds(garbageCollectInterval);
            System.GC.Collect();
            Debug.Log("<b>[СЕРВЕР]</b> Оперативная память успешно очищена.");
        }
    }

    public override void OnNetworkDespawn()
    {
        if (voidCheckCoroutine != null) StopCoroutine(voidCheckCoroutine);
    }
}
