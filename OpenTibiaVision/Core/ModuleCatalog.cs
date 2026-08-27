using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenTibiaVision.Core;

/// <summary>
/// Discovers <see cref="IFeatureModule"/> implementors by reflection over the app assembly.
/// A module is any non-abstract class implementing the interface with a public parameterless
/// constructor. This is what makes the parallel feature tracks drop-in: add a class under
/// Features\, and the shell picks it up with no registration edits.
/// </summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<IFeatureModule> Discover()
    {
        var modules = new List<IFeatureModule>();

        Type[] types;
        try
        {
            types = Assembly.GetExecutingAssembly().GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }

        foreach (Type type in types)
        {
            if (type.IsAbstract || type.IsInterface)
                continue;
            if (!typeof(IFeatureModule).IsAssignableFrom(type))
                continue;
            if (type.GetConstructor(Type.EmptyTypes) is null)
                continue;

            try
            {
                if (Activator.CreateInstance(type) is IFeatureModule module)
                    modules.Add(module);
            }
            catch
            {
                // A module that throws in its ctor is skipped rather than sinking the app.
            }
        }

        // Deterministic order: by explicit Order, then Title. Undeclared modules default to
        // Order 1000 and thus sort after the declared features, alphabetically among themselves.
        return modules
            .OrderBy(m => m.Order)
            .ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
