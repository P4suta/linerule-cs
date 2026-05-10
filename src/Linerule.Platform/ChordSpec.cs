namespace Linerule.Platform;

/// <summary>A parsed chord specification: modifier set + final key.</summary>
public sealed record ChordSpec(Modifiers Modifiers, KeyCode Key);
