using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class SelectToEditInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    private TMP_InputField _input;
    private Action _onSelect;
    private bool _pressed;
    private Vector2 _pressPos;
    private float _threshold;

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

        if (_input != null && _input.interactable && _input.isFocused)
            return;

        _onSelect?.Invoke();
    }

    public void Initialize(TMP_InputField input, float dragThresholdPx, Action onSelect)
    {
        _input = input;
        _threshold = dragThresholdPx;
        _onSelect = onSelect;
    }
}