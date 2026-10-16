namespace SecureOps.Ui.Services;

/// <summary>
/// One step of an on-screen guided tour (SoGuidedTour). The step points at an element carrying <c>data-tour="Target"</c>; it never
/// clicks, opens or changes anything. <paramref name="Allowed"/> comes from the server's answer (AccessSnapshot or record permissions):
/// a step the caller may not take is not shown and not counted.
/// </summary>
/// <param name="Target">Value of the <c>data-tour</c> attribute on the highlighted element.</param>
/// <param name="Title">Short name of the area.</param>
/// <param name="What">One sentence: what this area is or does.</param>
/// <param name="Why">One sentence: why the person needs it.</param>
/// <param name="Allowed">False hides the step for this caller.</param>
public sealed record SoTourStep(string Target, string Title, string What, string Why, bool Allowed = true);

/// <summary>
/// Pure state of a guided tour: which allowed step is shown, which steps were skipped because their area is not on the screen, and
/// whether there is a step before or after. Presence is the browser's answer for every allowed step, re-read on each move; a missing or
/// short answer counts as "not on the screen" so the tour never points at nothing.
/// </summary>
public sealed class GuidedTourState
{
    private bool[] _present = [];

    /// <summary>Creates the state from the page's steps; steps the caller may not take are dropped here.</summary>
    public GuidedTourState(IEnumerable<SoTourStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        Steps = [.. steps.Where(s => s.Allowed)];
    }

    /// <summary>The allowed steps, in order.</summary>
    public IReadOnlyList<SoTourStep> Steps { get; }

    /// <summary>True while the tour box is shown.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Index of the shown step in <see cref="Steps"/>; -1 when closed or when no step's area is on the screen.</summary>
    public int Index { get; private set; } = -1;

    /// <summary>The shown step, if any.</summary>
    public SoTourStep? Current => Index >= 0 ? Steps[Index] : null;

    /// <summary>Steps passed over by the last move because their area is not on the screen.</summary>
    public IReadOnlyList<SoTourStep> Skipped { get; private set; } = [];

    /// <summary>Steps after the shown one whose area is not on the screen, when no later step can be shown.</summary>
    public IReadOnlyList<SoTourStep> MissingAfter => HasNext || Index < 0 ? [] : [.. Steps.Skip(Index + 1)];

    /// <summary>True when an earlier step's area is on the screen.</summary>
    public bool HasPrevious => Find(Index - 1, -1) >= 0;

    /// <summary>True when a later step's area is on the screen.</summary>
    public bool HasNext => Index >= 0 && Find(Index + 1, 1) >= 0;

    /// <summary>Opens the tour on the first step whose area is on the screen (or on "nothing to show" when none is).</summary>
    public void Start(IReadOnlyList<bool>? present)
    {
        Update(present);
        IsOpen = true;
        int first = Find(0, 1);
        Index = first;
        Skipped = first < 0 ? [] : [.. Steps.Take(first)];
    }

    /// <summary>Moves to the next step whose area is on the screen. Returns false (and closes) when there is none.</summary>
    public bool Next(IReadOnlyList<bool>? present) => Move(present, 1);

    /// <summary>Moves to the previous step whose area is on the screen. Returns false (and stays) when there is none.</summary>
    public bool Back(IReadOnlyList<bool>? present) => Move(present, -1);

    /// <summary>Closes the tour.</summary>
    public void Close()
    {
        IsOpen = false;
        Index = -1;
        Skipped = [];
    }

    private bool Move(IReadOnlyList<bool>? present, int direction)
    {
        if (!IsOpen || Index < 0)
        {
            return false;
        }

        Update(present);
        int target = Find(Index + direction, direction);
        if (target < 0)
        {
            if (direction > 0)
            {
                Close();
            }

            return false;
        }

        int from = Math.Min(Index, target) + 1;
        Skipped = [.. Steps.Skip(from).Take(Math.Abs(target - Index) - 1)];
        if (direction < 0)
        {
            Skipped = [.. Skipped.Reverse()];
        }

        Index = target;
        return true;
    }

    private void Update(IReadOnlyList<bool>? present)
    {
        _present = new bool[Steps.Count];
        for (int i = 0; i < _present.Length && present is not null && i < present.Count; i++)
        {
            _present[i] = present[i];
        }
    }

    private int Find(int start, int direction)
    {
        for (int i = start; i >= 0 && i < Steps.Count; i += direction)
        {
            if (_present[i])
            {
                return i;
            }
        }

        return -1;
    }
}
