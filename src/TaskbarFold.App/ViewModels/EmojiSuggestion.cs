using System.Windows.Media;

namespace TaskbarFold.App.ViewModels;

/// <param name="Emoji">The character sequence itself.</param>
/// <param name="Name">Tooltip text.</param>
/// <param name="Image">
/// Pre-rendered picture. WPF cannot draw colour emoji, so the picker shows bitmaps
/// produced by the same Direct2D path that writes the icon.
/// </param>
public sealed record EmojiSuggestion(string Emoji, string Name, ImageSource? Image);

/// <summary>The shortlist offered in the group editor, covering the usual group themes.</summary>
public static class EmojiCatalog
{
    public static IReadOnlyList<(string Emoji, string Name)> Defaults { get; } =
    [
        ("\U0001F4BC", "Work"),
        ("\U0001F3A8", "Design"),
        ("\U0001F3AE", "Games"),
        ("\U0001F6E0️", "Tools"),
        ("\U0001F4BB", "Development"),
        ("\U0001F4AC", "Chat"),
        ("\U0001F3B5", "Music"),
        ("\U0001F3AC", "Video"),
        ("\U0001F4C1", "Files"),
        ("\U0001F4F7", "Photos"),
        ("\U0001F4CA", "Office"),
        ("\U0001F310", "Internet"),
        ("\U0001F4DA", "Reading"),
        ("\U0001F680", "Launch"),
        ("⚙️", "System"),
        ("\U0001F510", "Security"),
        ("\U0001F6D2", "Shopping"),
        ("❤️", "Favourites"),
        ("⭐", "Starred"),
        ("\U0001F525", "Hot"),
    ];
}
