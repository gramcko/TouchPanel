using System.Reflection;
using System.Runtime.CompilerServices;
using TouchPanel.Actions;

namespace TouchPanel.Tests.Architecture;

/// <summary>
/// Layering gate (plan section 6.1, RNF-010/RF-053/RF-062/M2-RNF-125): the <c>Actions/</c>,
/// <c>Config/</c>, <c>Monitoring/</c>, and <c>Input/</c> namespaces are plain, WPF-free C# so they
/// stay unit-testable without a
/// UI. The first check reflects over every type in those namespaces (including nested namespaces
/// such as <c>Config.Validation</c>) and fails when a member signature, base type, or interface
/// exposes a WPF assembly type. The second check proves the LibreHardwareMonitor dependency is
/// confined to the single adapter type required by RF-062.
/// </summary>
public sealed class LayerPurityTests
{
    private const string AdapterTypeName = "TouchPanel.Monitoring.LibreHardwareMonitorAdapter";

    private static readonly string[] WpfAssemblies =
    [
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
    ];

    private static readonly string[] HardwareLibraryAssemblies =
    [
        "LibreHardwareMonitorLib",
    ];

    [Theory]
    [InlineData("TouchPanel.Actions")]
    [InlineData("TouchPanel.Config")]
    [InlineData("TouchPanel.Monitoring")]
    [InlineData("TouchPanel.Input")]
    [InlineData("TouchPanel.Services.Config")]
    public void WpfFreeLayers_DoNotExposeWpfTypesInTheirMembers(string namespacePrefix)
    {
        var assembly = typeof(IActionExecutor).Assembly;
        var violations = new List<string>();

        var candidates = assembly.GetTypes()
            .Where(type => IsInNamespace(type, namespacePrefix))
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false));

        foreach (var type in candidates)
        {
            InspectType(type, violations, WpfAssemblies);
        }

        Assert.True(
            violations.Count == 0,
            $"{namespacePrefix} must not reference WPF types:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void LibreHardwareMonitorTypes_AreConfinedToTheAdapter()
    {
        var assembly = typeof(IActionExecutor).Assembly;
        var violations = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            // RF-062: the adapter is the single intentional seam with the hardware library; every
            // other type in the application must stay library-agnostic.
            if (string.Equals(type.FullName, AdapterTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            InspectType(type, violations, HardwareLibraryAssemblies);
        }

        Assert.True(
            violations.Count == 0,
            $"LibreHardwareMonitor types must not appear outside {AdapterTypeName}:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static bool IsInNamespace(Type type, string namespacePrefix) =>
        type.Namespace is { } ns
        && (string.Equals(ns, namespacePrefix, StringComparison.Ordinal)
            || ns.StartsWith(namespacePrefix + ".", StringComparison.Ordinal));

    private static void InspectType(Type type, List<string> violations, string[] forbiddenAssemblies)
    {
        const BindingFlags flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        InspectReference(type.BaseType, type, "base type", violations, forbiddenAssemblies);

        foreach (var implemented in type.GetInterfaces())
        {
            InspectReference(implemented, type, "interface", violations, forbiddenAssemblies);
        }

        foreach (var field in type.GetFields(flags))
        {
            InspectReference(field.FieldType, type, $"field '{field.Name}'", violations, forbiddenAssemblies);
        }

        foreach (var property in type.GetProperties(flags))
        {
            InspectReference(property.PropertyType, type, $"property '{property.Name}'", violations, forbiddenAssemblies);
        }

        foreach (var method in type.GetMethods(flags))
        {
            InspectReference(method.ReturnType, type, $"method '{method.Name}' return", violations, forbiddenAssemblies);

            foreach (var parameter in method.GetParameters())
            {
                InspectReference(parameter.ParameterType, type, $"method '{method.Name}' parameter '{parameter.Name}'", violations, forbiddenAssemblies);
            }
        }

        foreach (var constructor in type.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                InspectReference(parameter.ParameterType, type, $"constructor parameter '{parameter.Name}'", violations, forbiddenAssemblies);
            }
        }
    }

    private static void InspectReference(
        Type? reference,
        Type owner,
        string location,
        List<string> violations,
        string[] forbiddenAssemblies)
    {
        if (reference is null || reference.IsGenericParameter)
        {
            return;
        }

        if (reference.IsArray || reference.IsByRef || reference.IsPointer)
        {
            InspectReference(reference.GetElementType(), owner, location, violations, forbiddenAssemblies);
            return;
        }

        if (forbiddenAssemblies.Contains(reference.Assembly.GetName().Name, StringComparer.Ordinal))
        {
            violations.Add($"{owner.FullName}: {location} uses {reference}");
        }

        if (reference.IsGenericType)
        {
            foreach (var argument in reference.GetGenericArguments())
            {
                InspectReference(argument, owner, location, violations, forbiddenAssemblies);
            }
        }
    }
}
