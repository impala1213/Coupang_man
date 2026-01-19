// Assets/Scripts/UI/PlayerHealthUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple player health HUD.
/// - Binds to a Health component (auto by PlayerController).
/// - Updates a Slider and optional text when values change.
/// - Uses a lightweight hash check each frame so it also reacts to Heal() (no event).
/// </summary>
[DisallowMultipleComponent]
public class PlayerHealthUI : MonoBehaviour
{
    [Header("Binding")]
    [Tooltip("Optional: Player controller used to resolve Health automatically.")]
    public PlayerController player;

    [Tooltip("Optional: Explicit Health reference. If null, resolved from PlayerController.")]
    public Health health;

    [Tooltip("Auto-resolve references in Awake/OnEnable.")]
    public bool autoBind = true;

    [Header("UI")]
    public Slider healthSlider;

    [Tooltip("Optional text label (e.g., '75 / 100').")]
    public TextMeshProUGUI healthText;

    [Tooltip("Optional canvas group for show/hide. Recommended.")]
    public CanvasGroup canvasGroup;

    [Header("Behavior")]
    [Tooltip("Hide the UI when health is full (only works when CanvasGroup is assigned).")]
    public bool hideWhenFull = false;

    [Tooltip("If true, the slider uses normalized (0..1). Otherwise, uses (0..maxHealth).")]
    public bool useNormalizedSlider = false;

    [Tooltip("If true, the slider value is smoothed.")]
    public bool smoothValue = false;

    [Min(0f)]
    public float smoothSpeed = 12f;

    private int _lastHash = int.MinValue;
    private float _displayValue;

    void Awake()
    {
        if (autoBind)
            ResolveReferences();

        ResolveUIReferences();

        Subscribe();
        ForceRefresh();
    }

    void OnEnable()
    {
        if (autoBind)
            ResolveReferences();

        Subscribe();
        ForceRefresh();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void Update()
    {
        int h = ComputeHash();
        if (h != _lastHash)
        {
            _lastHash = h;
            RefreshImmediate();
        }

        if (smoothValue && health && healthSlider)
        {
            float target = GetTargetSliderValue();
            _displayValue = Mathf.Lerp(_displayValue, target, 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime));
            ApplySliderValue(_displayValue);
        }
    }

    public void ForceRefresh()
    {
        _lastHash = int.MinValue;
        RefreshImmediate();
    }

    private void ResolveReferences()
    {
        if (!player)
            player = FindFirstObjectByType<PlayerController>();

        if (!health && player)
            health = player.GetComponent<Health>();
    }

    private void ResolveUIReferences()
    {
        if (!healthSlider)
            healthSlider = GetComponentInChildren<Slider>(true);

        if (!healthText)
            healthText = GetComponentInChildren<TextMeshProUGUI>(true);

        if (!canvasGroup)
            canvasGroup = GetComponent<CanvasGroup>();
    }

    private void Subscribe()
    {
        if (!health) return;

        health.OnDamaged -= OnDamaged;
        health.OnDeath -= OnDeath;

        health.OnDamaged += OnDamaged;
        health.OnDeath += OnDeath;
    }

    private void Unsubscribe()
    {
        if (!health) return;

        health.OnDamaged -= OnDamaged;
        health.OnDeath -= OnDeath;
    }

    private void OnDamaged(Health h, int amount)
    {
        // UI is also updated via hash polling; this makes the response instant.
        ForceRefresh();
    }

    private void OnDeath(Health h)
    {
        ForceRefresh();
    }

    private int ComputeHash()
    {
        if (!health)
            return 0;

        unchecked
        {
            int max = Mathf.Max(1, health.maxHealth);
            int cur = Mathf.Clamp(health.currentHealth, 0, max);
            int dead = health.IsDead() ? 1 : 0;

            int h = 17;
            h = h * 31 + max;
            h = h * 31 + cur;
            h = h * 31 + dead;
            return h;
        }
    }

    private void RefreshImmediate()
    {
        if (!health)
        {
            SetVisible(true);

            if (healthSlider)
            {
                healthSlider.minValue = 0f;
                healthSlider.maxValue = useNormalizedSlider ? 1f : 1f;
                healthSlider.value = 0f;
            }

            if (healthText)
                healthText.text = "";

            return;
        }

        int max = Mathf.Max(1, health.maxHealth);
        int cur = Mathf.Clamp(health.currentHealth, 0, max);

        bool isFull = (cur >= max);
        bool shouldShow = !hideWhenFull || !isFull;

        SetVisible(shouldShow);

        if (healthSlider)
        {
            if (useNormalizedSlider)
            {
                healthSlider.minValue = 0f;
                healthSlider.maxValue = 1f;
                _displayValue = Mathf.Clamp01((float)cur / max);
            }
            else
            {
                healthSlider.minValue = 0f;
                healthSlider.maxValue = max;
                _displayValue = cur;
            }

            if (!smoothValue)
                ApplySliderValue(_displayValue);
        }

        if (healthText)
            healthText.text = $"{cur} / {max}";
    }

    private float GetTargetSliderValue()
    {
        if (!health) return 0f;

        int max = Mathf.Max(1, health.maxHealth);
        int cur = Mathf.Clamp(health.currentHealth, 0, max);

        if (useNormalizedSlider)
            return Mathf.Clamp01((float)cur / max);

        return cur;
    }

    private void ApplySliderValue(float v)
    {
        if (!healthSlider) return;
        healthSlider.value = v;
    }

    private void SetVisible(bool visible)
    {
        if (!canvasGroup) return;

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.blocksRaycasts = visible;
        canvasGroup.interactable = visible;
    }
}
