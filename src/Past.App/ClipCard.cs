using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Past.Core;
using Windows.UI;

namespace Past.App;

/// <summary>
/// Presentation wrapper around a <see cref="Clip"/> for the horizontal card strip.
/// Keeps display concerns (type badge, colour, relative age, char count, selection
/// styling) out of the domain.
/// </summary>
internal sealed class ClipCard : INotifyPropertyChanged
{
    private const int BodyMaxChars = 280;
    private bool _isHighlighted;

    public ClipCard(Clip clip)
    {
        Clip = clip;
        Kind = Classify(clip.Content);
        HeaderBrush = new SolidColorBrush(ColorFor(Kind));

        var body = clip.Content.Trim();
        Body = body.Length > BodyMaxChars ? body[..BodyMaxChars] + "…" : body;

        CharCount = clip.Content.Length == 1 ? "1 char" : $"{clip.Content.Length} chars";
        SourceApp = clip.SourceApp ?? "unknown";
        Age = RelativeAge(clip.LastUsedUtc);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Clip Clip { get; }
    public string Kind { get; }
    public string Body { get; }
    public string CharCount { get; }
    public string SourceApp { get; }
    public string Age { get; }
    public SolidColorBrush HeaderBrush { get; }

    /// <summary>
    /// Purely visual. Deliberately NOT the same as ListView selection: a card is only
    /// highlighted when the strip actually holds focus, so we never show a "selected"
    /// card while the user is typing in the search box.
    /// </summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted == value)
                return;
            _isHighlighted = value;
            Raise();
            Raise(nameof(CardBorderBrush));
            Raise(nameof(CardBorderThickness));
            Raise(nameof(CardElevation));
        }
    }

    public Brush CardBorderBrush => _isHighlighted ? Accent : Stroke;

    public Thickness CardBorderThickness => new(_isHighlighted ? 2 : 1);

    /// <summary>Z-translation drives the ThemeShadow: the highlighted card lifts off the strip.</summary>
    public Vector3 CardElevation => _isHighlighted ? new Vector3(0, 0, 32) : Vector3.Zero;

    private static Brush Accent => (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
    private static Brush Stroke => (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>P0 is text-only, so these are text sub-kinds. File/Image arrive with v2 capture.</summary>
    private static string Classify(string content)
    {
        var t = content.Trim();

        if ((t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
            !t.Contains(' ') && !t.Contains('\n'))
            return "LINK";

        if (LooksLikeCode(t))
            return "CODE";

        return "TEXT";
    }

    private static bool LooksLikeCode(string t)
    {
        string[] markers = ["{", "};", "=>", "()", "func ", "def ", "class ", "public ", "const ", "import ", "#include", "</"];
        var hits = markers.Count(m => t.Contains(m, StringComparison.Ordinal));
        return hits >= 2;
    }

    private static Color ColorFor(string kind) => kind switch
    {
        "LINK" => Color.FromArgb(255, 13, 148, 136),  // teal
        "CODE" => Color.FromArgb(255, 124, 58, 237),  // violet
        _ => Color.FromArgb(255, 79, 70, 229),        // indigo
    };

    private static string RelativeAge(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d < TimeSpan.FromSeconds(60)) return "just now";
        if (d < TimeSpan.FromMinutes(60)) return $"{(int)d.TotalMinutes}m ago";
        if (d < TimeSpan.FromHours(24)) return $"{(int)d.TotalHours}h ago";
        return $"{(int)d.TotalDays}d ago";
    }
}
