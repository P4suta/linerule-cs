namespace Linerule.Platform;

/// <summary>
/// Symbolic key identity that the chord parser can produce. Closed sum.
/// The Windows-side adapter (in <c>Linerule.Platform.Windows</c>) maps these
/// to virtual-key codes for <c>RegisterHotKey</c>; other backends would map
/// to their own scancode space.
/// </summary>
public abstract record KeyCode
{
    private protected KeyCode() { }

    /// <summary>Letter key, ASCII <c>'A'..='Z'</c> only.</summary>
    public sealed record Letter(byte Code) : KeyCode;

    public sealed record BracketLeft : KeyCode { public static BracketLeft Instance { get; } = new(); }

    public sealed record BracketRight : KeyCode { public static BracketRight Instance { get; } = new(); }

    public sealed record Minus : KeyCode { public static Minus Instance { get; } = new(); }

    public sealed record Equal : KeyCode { public static Equal Instance { get; } = new(); }

    public sealed record ArrowUp : KeyCode { public static ArrowUp Instance { get; } = new(); }

    public sealed record ArrowDown : KeyCode { public static ArrowDown Instance { get; } = new(); }

    public sealed record ArrowLeft : KeyCode { public static ArrowLeft Instance { get; } = new(); }

    public sealed record ArrowRight : KeyCode { public static ArrowRight Instance { get; } = new(); }
}
