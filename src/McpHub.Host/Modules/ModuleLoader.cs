using System.Reflection;
using System.Runtime.Loader;
using McpHub.Core.Modules;

namespace McpHub.Host.Modules;

/// <summary>Finds <see cref="IIntegrationModule"/> implementations in <c>McpHub.Modules.*.dll</c> next to the host.</summary>
internal static class ModuleLoader
{
    private const string AssemblyPattern = "McpHub.Modules.*.dll";

    public static IReadOnlyList<IIntegrationModule> Discover(string? directory = null)
    {
        var modules = new List<IIntegrationModule>();

        foreach (var path in Directory.EnumerateFiles(directory ?? AppContext.BaseDirectory, AssemblyPattern))
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);

            foreach (var type in GetModuleTypes(assembly))
            {
                modules.Add((IIntegrationModule)Activator.CreateInstance(type)!);
            }
        }

        var duplicate = modules
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Two integration modules declare the name '{duplicate.Key}'.");
        }

        return modules;
    }

    private static IEnumerable<Type> GetModuleTypes(Assembly assembly) =>
        assembly.GetTypes().Where(t =>
            t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationModule).IsAssignableFrom(t));
}
