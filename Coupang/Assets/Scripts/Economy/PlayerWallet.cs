using System;
using UnityEngine;

/// <summary>
/// Simple persistent money holder.
/// Put this on playerRoot (or any object moved by GameSession).
/// </summary>
[DisallowMultipleComponent]
public class PlayerWallet : MonoBehaviour
{
    [SerializeField] private int money;

    public int Money => money;
    public event Action<int> OnMoneyChanged;

    public void SetMoney(int value)
    {
        money = Mathf.Max(0, value);
        OnMoneyChanged?.Invoke(money);
    }

    public void AddMoney(int delta)
    {
        SetMoney(money + delta);
    }
}
