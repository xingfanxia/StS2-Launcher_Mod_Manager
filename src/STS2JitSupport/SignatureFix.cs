using System.Reflection;

namespace STS2JitSupport;

public static class SignatureFix
{
    // MonoMod's reflection importer drops return modifiers, so Mono cannot resolve init setters.
    // Use the importer's own merged Cecil types without binding this assembly to its private API.
    public static void PreserveReturnModifiers(object importer, MethodBase source, object target)
    {
        if (source is not MethodInfo method) return;
        var required = method.ReturnParameter.GetRequiredCustomModifiers();
        var optional = method.ReturnParameter.GetOptionalCustomModifiers();
        if (required.Length == 0 && optional.Length == 0) return;
        var import = importer.GetType().GetMethods().Single(m => m.Name == "ImportReference" &&
            m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(Type));
        var property = target.GetType().GetProperty("ReturnType")!;
        object value = property.GetValue(target)!;
        foreach (var (modifiers, name) in new[] { (optional, "OptionalModifierType"), (required, "RequiredModifierType") })
        {
            Type wrapper = importer.GetType().Assembly.GetType("Mono.Cecil." + name, true)!;
            foreach (Type modifier in modifiers.Reverse())
                value = Activator.CreateInstance(wrapper, import.Invoke(importer, new[] { modifier, target }), value)!;
        }
        property.SetValue(target, value);
    }
}
