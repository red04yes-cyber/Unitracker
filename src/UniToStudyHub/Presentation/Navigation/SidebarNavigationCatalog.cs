using System.ComponentModel;

namespace UniToStudyHub.Presentation.Navigation;

public sealed record SidebarNavigationDefinition(
    string Title,
    string Description,
    string Glyph,
    int PageIndex,
    bool IsSectionStart = false);

/// <summary>
/// A navigation entry with a small observable selection state for the WPF shell.
/// The page index deliberately mirrors the stable TabItem order in MainWindow.xaml.
/// </summary>
public sealed class SidebarNavigationItem : INotifyPropertyChanged
{
    private bool _isSelected;

    internal SidebarNavigationItem(SidebarNavigationDefinition definition)
    {
        Definition = definition;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => Definition.Title;
    public string Description => Definition.Description;
    public string Glyph => Definition.Glyph;
    public int PageIndex => Definition.PageIndex;
    public bool IsSectionStart => Definition.IsSectionStart;

    public SidebarNavigationDefinition Definition { get; }

    public bool IsSelected
    {
        get => _isSelected;
        private set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    internal void SetSelected(bool selected) => IsSelected = selected;
}

/// <summary>
/// Stable navigation metadata for the existing MainWindow pages.
/// Page 6 remains the profile page; page 7 is the settings page.
/// </summary>
public static class SidebarNavigationCatalog
{
    private static readonly SidebarNavigationDefinition[] Definitions =
    [
        new("Oggi", "La tua dashboard", "\\uE80F", 0),
        new("Materie", "Corsi e archivio", "\\uE8A5", 1),
        new("Calendario", "Appelli e pianificazione", "\\uE787", 2),
        new("Studio Lab", "Focus e allenamento", "\\uE945", 3),
        new("Accademia", "Strumenti di apprendimento", "\\uE82D", 4),
        new("Progressi", "Statistiche di carriera", "\\uE9D2", 5),
        new("Profilo", "Carriera e obiettivi", "\\uE77B", 6, IsSectionStart: true),
        new("Impostazioni", "Profilo e integrazioni", "\\uE713", 7)
    ];

    public static IReadOnlyList<SidebarNavigationDefinition> Entries { get; } =
        Array.AsReadOnly(Definitions);

    public static IReadOnlyList<SidebarNavigationItem> CreateItems() =>
        Definitions.Select(static definition => new SidebarNavigationItem(definition)).ToArray();
}
