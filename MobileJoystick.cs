using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class MobileJoystick : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Компоненты")]
    public Image background;
    public Image handle;

    public float Horizontal { get; private set; }
    public float Vertical { get; private set; }

    private Vector2 inputVector;
    private bool isDragging = false;

    private void Start()
    {
        if (background == null) background = GetComponent<Image>();
        if (handle == null && transform.childCount > 0) handle = transform.GetChild(0).GetComponent<Image>();

        // Жестко центрируем опорные точки UI элементов в коде, чтобы исключить уползание вниз
        if (background != null) background.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        if (handle != null)
        {
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            handle.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            handle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            handle.rectTransform.anchoredPosition = Vector2.zero;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isDragging = true;
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            background.rectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 localPoint
        );

        float radius = background.rectTransform.rect.width * 0.5f;
        if (radius <= 0) return;

        inputVector = localPoint / radius;
        if (inputVector.magnitude > 1f) inputVector = inputVector.normalized;

        Horizontal = inputVector.x;
        Vertical = inputVector.y;

        if (handle != null)
            handle.rectTransform.anchoredPosition = inputVector * radius;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isDragging = false;
        inputVector = Vector2.zero; // ИСПРАВЛЕНО: Принудительно сбрасываем внутренний вектор в ноль
        Horizontal = 0;
        Vertical = 0;

        if (handle != null)
            handle.rectTransform.anchoredPosition = Vector2.zero; // Возвращаем в абсолютный центр
    }
}
