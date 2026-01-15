/// <summary>
/// Optional interface for monsters that can be woken up.
/// Attach a component implementing this to your "strong" monster,
/// or rely on SendMessage("WakeUp") fallback.
/// </summary>
public interface IWakeable
{
    void WakeUp();
}
