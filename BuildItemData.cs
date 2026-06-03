using UnityEngine;

[CreateAssetMenu(fileName = "NewBuildItem", menuName = "Building/Build Item Data")]
public class BuildItemData : ScriptableObject
{
    public string itemName;
    public Sprite itemIcon;
    public GameObject solidPrefab;
    public GameObject ghostPrefab;

    // НАСТРОЕНО: Новое поле для кастомного внешнего вида (скина) NPC
    [Header("КАСТОМИЗАЦИЯ СКИНОГО МОДЕЛЯ")]
    [Tooltip("Если пусто — используется стандартный вид префаба. Если перетащить сюда другую 3D-модель, NPC примет её вид и пересчитает физику.")]
    public GameObject customSkinPrefab;
}
