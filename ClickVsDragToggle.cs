using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class ClickVsDragToggle : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    private Action<bool> _onChanged;
    private bool _pressed;
    private Vector2 _pressPos;
    private float _threshold;
    private bool _value;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;
        _pressed = true;
        _pressPos = eventData.position;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_pressed || eventData.button != PointerEventData.InputButton.Left)
            return;
        _pressed = false;

        if ((eventData.position - _pressPos).sqrMagnitude > _threshold * _threshold)
            return;

        _value = !_value;
        _onChanged?.Invoke(_value);
    }

    public void Initialize(bool initialValue, float dragThresholdPx, Action<bool> onChanged)
    {
        _value = initialValue;
        _threshold = dragThresholdPx;
        _onChanged = onChanged;
    }
}