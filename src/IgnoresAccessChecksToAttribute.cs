namespace System.Runtime.CompilerServices;

/// <summary>
/// The assembly publicizer emits [assembly: IgnoresAccessChecksTo("Assembly-CSharp")] but does not
/// supply the attribute type. REPOLib.dll happens to declare its own *internal* copy, which the
/// compiler binds to first and then rejects as inaccessible. Declaring a public one here wins:
/// types in the compiling assembly take precedence over imported ones.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class IgnoresAccessChecksToAttribute : Attribute
{
    public IgnoresAccessChecksToAttribute(string assemblyName)
    {
        AssemblyName = assemblyName;
    }

    public string AssemblyName { get; }
}
