using UnityEngine;
using UnityEngine.EventSystems;

public class MobileJumpButton : MonoBehaviour, IPointerDownHandler
{
    public bool JumpRequested { get; private set; }

    public void OnPointerDown(PointerEventData eventData)
    {
        JumpRequested = true;
    }

    // Метод вызывается контроллером игрока, когда прыжок успешно обработан
    public void ResetJumpRequest()
    {
        JumpRequested = false;
    }
}
