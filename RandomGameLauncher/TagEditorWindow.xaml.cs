using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using RandomGameLauncher.Services;

namespace RandomGameLauncher;

public partial class TagEditorWindow : Window
{
    public sealed class TagItem : INotifyPropertyChanged
    {
        public string Name { get; }

        bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public TagItem(string name, bool selected)
        {
            Name = name;
            _isSelected = selected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    readonly ObservableCollection<TagItem> _items;
    readonly ICollectionView _view;

    public IReadOnlyList<string> Tags { get; private set; } = Array.Empty<string>();

    readonly AppTheme _theme;
    readonly BackdropKind _backdrop;

    public TagEditorWindow(string gameName, IReadOnlyList<string> knownTags, IReadOnlyList<string> selectedTags, AppTheme theme, BackdropKind backdrop)
    {
        InitializeComponent();

        _theme = theme;
        _backdrop = backdrop;

        SourceInitialized += async (_, _) => await ThemeManager.ApplyAsync(this, RootHost, _theme, _backdrop, animate: false);

        TitleText.Text = $"Tags - {gameName}";

        var selected = new HashSet<string>(selectedTags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var merged = (knownTags ?? Array.Empty<string>())
            .Concat(selected)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _items = new ObservableCollection<TagItem>(merged.Select(t => new TagItem(t, selected.Contains(t))));
        TagsList.ItemsSource = _items;

        _view = CollectionViewSource.GetDefaultView(TagsList.ItemsSource);
        _view.Filter = o =>
        {
            if (o is not TagItem ti) return false;
            var q = (SearchBox.Text ?? "").Trim();
            if (q.Length == 0) return true;
            return ti.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
        };

        SearchBox.TextChanged += (_, _) => _view.Refresh();
        SearchBox.Focus();
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        var tag = TagService.NormalizeTags(NewTagBox.Text).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tag)) return;

        var existing = _items.FirstOrDefault(x => x.Name.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsSelected = true;
        }
        else
        {
            _items.Add(new TagItem(tag, selected: true));
        }

        NewTagBox.Text = "";
        _view.Refresh();
    }

    void Ok_Click(object sender, RoutedEventArgs e)
    {
        Tags = _items.Where(x => x.IsSelected).Select(x => x.Name).ToArray();
        DialogResult = true;
        Close();
    }
}
