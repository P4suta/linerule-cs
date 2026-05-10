using System.Runtime.InteropServices;

namespace Linerule.Config;

/// <summary>1-based <c>(line, column)</c> position. Both are 1-based (TextMate convention).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SourcePosition(int Line, int Column);
