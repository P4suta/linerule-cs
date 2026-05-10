namespace Linerule.Platform.Windows.Diagnostics;

/// <summary>
/// One structured field of a <see cref="LogEntry"/>. Target-typed
/// <c>new("k", v)</c> at call sites keeps the API ergonomic without
/// losing structure (no string interpolation flattening).
/// </summary>
public readonly record struct LogField(string Key, object? Value);
