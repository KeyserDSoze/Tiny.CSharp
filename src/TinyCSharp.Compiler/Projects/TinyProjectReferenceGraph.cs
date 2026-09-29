using System.Xml.Linq;

namespace TinyCSharp.Compiler.Projects;

public sealed record TinyProjectReferenceNode(
    string ProjectPath,
    int Depth,
    string AssemblyName,
    string RootNamespace);

public sealed class TinyProjectReferenceGraph
{
    public IReadOnlyList<TinyProjectReferenceNode> Load(string rootProjectPath)
    {
        rootProjectPath = Path.GetFullPath(rootProjectPath);

        var nodes = new Dictionary<string, TinyProjectReferenceNode>(
            StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string ProjectPath, int Depth)>();

        queue.Enqueue((rootProjectPath, 0));

        while (queue.Count > 0)
        {
            var (projectPath, depth) = queue.Dequeue();
            projectPath = Path.GetFullPath(projectPath);

            if (nodes.TryGetValue(projectPath, out var existing) &&
                existing.Depth <= depth)
            {
                continue;
            }

            var metadata = ReadMetadata(projectPath);
            nodes[projectPath] = new TinyProjectReferenceNode(
                projectPath,
                depth,
                metadata.AssemblyName,
                metadata.RootNamespace);

            foreach (var referencedProject in ReadProjectReferences(projectPath))
            {
                queue.Enqueue((referencedProject, depth + 1));
            }
        }

        return nodes.Values
            .OrderBy(node => node.Depth)
            .ThenBy(node => node.ProjectPath, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> ReadProjectReferences(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            yield break;
        }

        var document = XDocument.Load(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;

        foreach (var element in document
                     .Descendants()
                     .Where(element => element.Name.LocalName == "ProjectReference"))
        {
            var include = element.Attribute("Include")?.Value?.Trim();

            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            yield return Path.GetFullPath(
                Path.Combine(projectDirectory, include));
        }
    }

    private static (string AssemblyName, string RootNamespace) ReadMetadata(
        string projectPath)
    {
        var fallback = Path.GetFileNameWithoutExtension(projectPath);

        if (!File.Exists(projectPath))
        {
            return (fallback, fallback);
        }

        var document = XDocument.Load(projectPath);
        var assemblyName = document
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "AssemblyName")
            ?.Value
            ?.Trim();
        var rootNamespace = document
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "RootNamespace")
            ?.Value
            ?.Trim();

        assemblyName = string.IsNullOrWhiteSpace(assemblyName)
            ? fallback
            : assemblyName;
        rootNamespace = string.IsNullOrWhiteSpace(rootNamespace)
            ? assemblyName
            : rootNamespace;

        return (assemblyName, rootNamespace);
    }
}
