// Polyfill so `record` and `init` compile on netstandard2.0 / net48.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
