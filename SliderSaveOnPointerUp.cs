using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class SliderSaveOnPointerUp : MonoBehaviour, IPointerUpHandler
{
    private Action _onSave;

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;
        _onSave?.Invoke();
    }

    public void Initialize(Action onSave)
    {
        _onSave = onSave;
    }
}