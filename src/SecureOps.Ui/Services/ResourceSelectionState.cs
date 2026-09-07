namespace SecureOps.Ui.Services;

/// <summary>Selection is limited to a confirmed visible page; changing its context discards selection.</summary>
public sealed class ResourceSelectionState
{
    private readonly HashSet<Guid> _selected = [];
    private Guid[] _page = [];
    /// <summary>Selected IDs in visible-page order, never hidden or unloaded references.</summary>
    public IReadOnlyList<Guid> Ids => _page.Where(_selected.Contains).ToArray();
    /// <summary>Whether every entry on a nonempty current page is selected.</summary>
    public bool All => _page.Length > 0 && _selected.Count == _page.Length;
    /// <summary>True only for a selected visible reference.</summary>
    public bool Contains(Guid id) => _selected.Contains(id);
    /// <summary>Installs a newly confirmed page and clears any old selection.</summary>
    public void SetPage(IEnumerable<Guid> ids)
    {
        _page = ids.Distinct().Take(100).ToArray();
        _selected.Clear();
    }
    /// <summary>Changes membership only for a currently visible ID.</summary>
    public void Toggle(Guid id, bool selected)
    {
        if (!_page.Contains(id))
        {
            return;
        }
        if (selected)
        {
            _selected.Add(id);
        }
        else
        {
            _selected.Remove(id);
        }
    }
    /// <summary>Selects only the current visible page, or clears its selection.</summary>
    public void SelectPage(bool selected)
    {
        _selected.Clear();
        if (selected)
        {
            _selected.UnionWith(_page);
        }
    }
    /// <summary>Invalidates both selection and the page scope while another query is pending.</summary>
    public void Clear()
    {
        _selected.Clear();
        _page = [];
    }
}
