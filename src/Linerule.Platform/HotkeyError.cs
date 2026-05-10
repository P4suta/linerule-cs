namespace Linerule.Platform;

public abstract record HotkeyError
{
    private protected HotkeyError() { }

    public sealed record AlreadyClaimed(ChordSpec Chord) : HotkeyError;

    public sealed record OsRefused(ChordSpec Chord, int Hresult) : HotkeyError;
}
