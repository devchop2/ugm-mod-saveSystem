using System;

namespace UGM.SaveSystem
{
    /// <summary>
    /// Marks a public field/property on an IDatabase or one of its data
    /// classes so it is excluded from FlatBuffer schema generation and
    /// (de)serialization. Useful for runtime-only state (caches, view-model
    /// fields) that lives on the same class as persistable data.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class NoSaveAttribute : Attribute { }
}
