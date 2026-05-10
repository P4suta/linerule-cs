using System.Runtime.CompilerServices;
using VerifyTests;

namespace Linerule.Core.Tests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init() => VerifierSettings.InitializePlugins();
}
