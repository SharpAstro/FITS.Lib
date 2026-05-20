// Polyfill for the AOT-trim suppression attribute which ships in net5.0+
// but not in netstandard2.0. Same fully-qualified name + internal visibility
// so net10.0 consumers pick up the BCL type via type-forwarding precedence
// while netstandard2.0 callers see this shim. AOT analysers honour the
// attribute regardless of which assembly declares it because they match
// by fully-qualified type name.

#if NETSTANDARD2_0
#nullable enable
namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Suppresses reporting of a specific rule violation, allowing multiple
    /// suppressions on a single code artifact. Identical contract to the BCL
    /// type in net5.0+; this shim exists only for the netstandard2.0 target.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    internal sealed class UnconditionalSuppressMessageAttribute : Attribute
    {
        public UnconditionalSuppressMessageAttribute(string category, string checkId)
        {
            Category = category;
            CheckId = checkId;
        }

        public string Category { get; }
        public string CheckId { get; }
        public string? Scope { get; set; }
        public string? Target { get; set; }
        public string? MessageId { get; set; }
        public string? Justification { get; set; }
    }
}
#endif
