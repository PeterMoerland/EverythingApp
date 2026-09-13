namespace EverythingApp.Services;

/// <summary>
/// Houdt bij welke pagina's recent bezocht zijn (voor de sidebar).
/// Demo: één gedeelde lijst. In productie per gebruiker opslaan.
/// </summary>
public class RecentPagesService
{
    private readonly object _lock = new();
    private readonly List<(string Url, string Titel)> _items = new();

    public IReadOnlyList<(string Url, string Titel)> Items
    {
        get { lock (_lock) return _items.ToList(); }
    }

    public void Voeg(string url, string titel)
    {
        lock (_lock)
        {
            _items.RemoveAll(i => i.Url == url);
            _items.Insert(0, (url, titel));
            if (_items.Count > 10) _items.RemoveRange(10, _items.Count - 10);
        }
    }
}
