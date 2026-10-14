using System.Reflection;
using System.Runtime.InteropServices;
using TouchPanel.Actions;
using TouchPanel.Actions.Handlers;
using TouchPanel.Config;
using TouchPanel.Theme;
using TouchPanel.ViewModels.Edit;

namespace TouchPanel.Tests.Architecture;

/// <summary>
/// M5 invariant guards (M5-RF-550/M5-RF-551, M5-RNF-552/553/556): P/Invoke stays confined to
/// <c>Interop/</c>, the new theme-default and hotkey-reference types are WPF-free, the hotkey action
/// has no second execution path (it runs only through <see cref="IActionExecutor"/>), and no M5
/// affordance activates the panel or the foreground window.
/// </summary>
public sealed class M5InvariantGuardTests
{
    private static readonly string[] WpfAssemblies =
    [
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
    ];

    [Fact]
    public void PInvoke_IsConfinedToTheInteropNamespace()
    {
        var assembly = typeof(IActionExecutor).Assembly;
        var offenders = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            foreach (var method in type.GetMethods(flags))
            {
                if (method.GetCustomAttribute<DllImportAttribute>() is not null
                    && (type.Namespace is null || !type.Namespace.StartsWith("TouchPanel.Interop", StringComparison.Ordinal)))
                {
                    offenders.Add($"{type.FullName}.{method.Name}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void M5NewTypes_AreWpfFree()
    {
        Type[] types =
        [
            typeof(ThemeAppearanceDefault),
            typeof(ThemeAppearanceDefaults),
            typeof(HotkeyActionResolver),
            typeof(RunHotkeyHandler),
            typeof(RunHotkeyActionConfig),
            typeof(HotkeyOption),
        ];

        var violations = new List<string>();
        foreach (var type in types)
        {
            InspectType(type, violations);
        }

        Assert.True(
            violations.Count == 0,
            "M5 types must stay WPF-free:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void HotkeyAction_HasNoSecondExecutionPath()
    {
        // M5-RF-521/M5-RNF-556: the handler depends only on the resolver and the shared executor.
        var parameters = typeof(RunHotkeyHandler)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IHotkeyActionResolver), parameters);
        Assert.Contains(typeof(IActionExecutor), parameters);

        // It is registered for exactly the additive hotkey discriminator.
        Assert.Equal(ActionTypes.Hotkey, new RunHotkeyHandler(
            new HotkeyActionResolver(() => []),
            new ThrowingExecutor()).ActionType);
    }

    [Fact]
    public void M5Affordances_DoNotExposeActivationCalls()
    {
        Type[] types =
        [
            typeof(ThemeAppearanceDefaults),
            typeof(HotkeyActionResolver),
            typeof(RunHotkeyHandler),
            typeof(HotkeyOption),
        ];

        foreach (var type in types)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.DoesNotContain(methods, method => method.Name is "Activate" or "SetForegroundWindow" or "Focus");
        }
    }

    private static void InspectType(Type type, List<string> violations)
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var property in type.GetProperties(flags))
        {
            InspectReference(property.PropertyType, type, $"property '{property.Name}'", violations);
        }

        foreach (var field in type.GetFields(flags))
        {
            InspectReference(field.FieldType, type, $"field '{field.Name}'", violations);
        }

        foreach (var method in type.GetMethods(flags))
        {
            InspectReference(method.ReturnType, type, $"method '{method.Name}' return", violations);
            foreach (var parameter in method.GetParameters())
            {
                InspectReference(parameter.ParameterType, type, $"method '{method.Name}' parameter '{parameter.Name}'", violations);
            }
        }

        foreach (var constructor in type.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                InspectReference(parameter.ParameterType, type, $"constructor parameter '{parameter.Name}'", violations);
            }
        }
    }

    private static void InspectReference(Type? reference, Type owner, string location, List<string> violations)
    {
        if (reference is null || reference.IsGenericParameter)
        {
            return;
        }

        if (reference.IsArray || reference.IsByRef || reference.IsPointer)
        {
            InspectReference(reference.GetElementType(), owner, location, violations);
            return;
        }

        if (WpfAssemblies.Contains(reference.Assembly.GetName().Name, StringComparer.Ordinal))
        {
            violations.Add($"{owner.FullName}: {location} uses {reference}");
        }

        if (reference.IsGenericType)
        {
            foreach (var argument in reference.GetGenericArguments())
            {
                InspectReference(argument, owner, location, violations);
            }
        }
    }

    /// <summary>An executor that is never called; only used to read the handler's action type.</summary>
    private sealed class ThrowingExecutor : IActionExecutor
    {
        public Task<ActionExecutionResult> ExecuteAsync(ActionConfig action, string? widgetId = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
