namespace Host.Web.Components.Pages.Shared.Wizard;

/// <summary>
/// Descriptor for one wizard step: the icon shown in the sidebar, the short title and
/// the longer "what's in it" subtitle, plus whether the step can be skipped without
/// validation. The shell consumes this list to render the sidebar and decide which
/// steps are jumpable vs. locked.
/// </summary>
public sealed record WizardStep(
    string Title,
    string Subtitle,
    string Icon,
    bool IsRequired,
    Func<bool> Validate);

/// <summary>Visual state of a sidebar step button.</summary>
public enum WizardStepState
{
    Upcoming,
    Active,
    Completed,
    Invalid
}

/// <summary>Top-level navigation strategy for the shell.</summary>
public enum WizardMode
{
    /// <summary>Validate-on-Next; only the last step shows Save (Add / Duplicate flows).</summary>
    Wizard,
    /// <summary>Free navigation between steps; Save is always visible (Edit flows).</summary>
    FreeNav
}
