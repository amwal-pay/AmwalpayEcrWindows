#if !NET5_0_OR_GREATER
// Lets `record` types and `init` accessors compile for netstandard2.0. The
// compiler only needs the type to exist; nothing reads it at run time.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
