// Assets/Scripts/Player/Energy.cs
using System;
using UnityEngine;

[DisallowMultipleComponent]
public class Energy : MonoBehaviour
{
    [Min(0f)]
    public float maxEnergy = 100f;

    [Min(0f)]
    public float currentEnergy;

    [Header("Drain")]
    [Tooltip("Energy drained per second for 1 drain unit.")]
    [Min(0f)]
    public float drainPerUnitPerSecond = 1f;

    [Tooltip("Current drain units (idle + action modifiers). Set by PlayerController.")]
    [Min(0)]
    [SerializeField] private int currentDrainUnits = 1;

    public event Action<Energy> OnDepleted;
    public event Action<Energy> OnRestored;
    public event Action<Energy, float, float> OnChanged;
    public event Action<Energy, int> OnDrainUnitsChanged;

    private bool isDepleted;

    public int CurrentDrainUnits => currentDrainUnits;
    public bool IsDepleted => isDepleted;

    void Awake()
    {
        currentEnergy = Mathf.Clamp(currentEnergy <= 0f ? maxEnergy : currentEnergy, 0f, maxEnergy);
        isDepleted = (currentEnergy <= 0f);
    }

    /// <summary>
    /// Drains energy using the current drain units.
    /// Call this once per frame (typically from PlayerController).
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        if (isDepleted) return;

        if (currentDrainUnits <= 0) return;

        float drain = drainPerUnitPerSecond * currentDrainUnits * deltaTime;
        if (drain <= 0f) return;

        ApplyDelta(-drain);
    }

    public void SetDrainUnits(int units)
    {
        units = Mathf.Max(0, units);
        if (units == currentDrainUnits) return;

        currentDrainUnits = units;
        OnDrainUnitsChanged?.Invoke(this, currentDrainUnits);
    }

    public void AddEnergy(float amount)
    {
        if (amount <= 0f) return;
        ApplyDelta(amount);
    }

    public bool SpendEnergy(float amount)
    {
        if (amount <= 0f) return true;
        if (currentEnergy < amount) return false;

        ApplyDelta(-amount);
        return true;
    }

    private void ApplyDelta(float delta)
    {
        float prev = currentEnergy;
        currentEnergy = Mathf.Clamp(currentEnergy + delta, 0f, maxEnergy);

        if (!Mathf.Approximately(prev, currentEnergy))
            OnChanged?.Invoke(this, currentEnergy, maxEnergy);

        if (!isDepleted && currentEnergy <= 0f)
        {
            isDepleted = true;
            OnDepleted?.Invoke(this);
        }
        else if (isDepleted && currentEnergy > 0f)
        {
            isDepleted = false;
            OnRestored?.Invoke(this);
        }
    }
}
