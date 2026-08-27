namespace UtevoLux.Shell;

/// <summary>Shell affordances the built-in Settings page drives (UI scale live control).</summary>
public interface IShellController
{
    double UiScale { get; }
    void SetUiScale(double scale);
    void StepUiScale(int direction); // +1 / -1
    void ResetUiScale();
}
