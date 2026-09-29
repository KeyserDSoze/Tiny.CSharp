using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using TinyCSharp.Compiler.Compilation;
using TinyCSharp.Compiler.Diagnostics;

namespace TinyCSharp.Compiler.Projects;

public sealed record TinyMetadataReferenceInfo(
    string Path,
    string AssemblyName,
    bool IsDirect,
    string Origin);

public sealed record TinyMetadataReferenceLoadResult(
    IReadOnlyList<TinyMetadataReferenceInfo> References,
    IReadOnlyList<TinyDiagnostic> Diagnostics);

public sealed class TinyProjectMetadataReferenceLoader
{
    public TinyMetadataReferenceLoadResult Load(
        string projectPath,
        bool warnWhenAssetsMissing = true)
    {
        projectPath = Path.GetFullPath(projectPath);

        var references = new List<TinyMetadataReferenceInfo>();
        var diagnostics = new List<TinyDiagnostic>();
        var directPackageIds = ReadDirectPackageIds(projectPath);

        references.AddRange(ReadHintPathReferences(projectPath));

        var projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
        var assetsPath = Path.Combine(
            projectDirectory,
            "obj",
            "project.assets.json");

        if (!File.Exists(assetsPath))
        {
            if (warnWhenAssetsMissing && directPackageIds.Count > 0)
            {
                diagnostics.Add(new TinyDiagnostic(
                    TinyDiagnosticSeverity.Warning,
                    $"NuGet assets file '{assetsPath}' was not found. " +
                    "Package types cannot be indexed until the project has been restored.",
                    projectPath,
                    1,
                    1,
                    Code: TinyDiagnosticCodes.ProjectAssetsUnavailable));
            }

            return new TinyMetadataReferenceLoadResult(
                Deduplicate(references),
                diagnostics);
        }

        try
        {
            using var stream = File.OpenRead(assetsPath);
            using var json = JsonDocument.Parse(stream);

            if (!json.RootElement.TryGetProperty("targets", out var targets) ||
                !json.RootElement.TryGetProperty("libraries", out var libraries) ||
                !json.RootElement.TryGetProperty("packageFolders", out var packageFolders))
            {
                return new TinyMetadataReferenceLoadResult(
                    Deduplicate(references),
                    diagnostics);
            }

            var target = SelectTarget(targets);

            if (target is null)
            {
                return new TinyMetadataReferenceLoadResult(
                    Deduplicate(references),
                    diagnostics);
            }

            var packageRoots = packageFolders
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray();

            foreach (var libraryTarget in target.Value.Value.EnumerateObject())
            {
                if (!libraries.TryGetProperty(
                        libraryTarget.Name,
                        out var libraryInfo) ||
                    !libraryInfo.TryGetProperty(
                        "type",
                        out var libraryType) ||
                    !string.Equals(
                        libraryType.GetString(),
                        "package",
                        StringComparison.OrdinalIgnoreCase) ||
                    !libraryInfo.TryGetProperty(
                        "path",
                        out var libraryPathElement) ||
                    !libraryTarget.Value.TryGetProperty(
                        "compile",
                        out var compileAssets))
                {
                    continue;
                }

                var libraryPath = libraryPathElement.GetString();

                if (string.IsNullOrWhiteSpace(libraryPath))
                {
                    continue;
                }

                var packageId = libraryTarget.Name
                    .Split('/', 2, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? libraryTarget.Name;
                var isDirect = directPackageIds.Contains(packageId);

                foreach (var asset in compileAssets.EnumerateObject())
                {
                    if (!asset.Name.EndsWith(
                            ".dll",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var assemblyPath = ResolvePackageAsset(
                        packageRoots,
                        libraryPath,
                        asset.Name);

                    if (assemblyPath is null)
                    {
                        continue;
                    }

                    var assemblyName = TryReadAssemblyName(assemblyPath);

                    if (assemblyName is null)
                    {
                        continue;
                    }

                    references.Add(new TinyMetadataReferenceInfo(
                        assemblyPath,
                        assemblyName,
                        isDirect,
                        packageId));
                }
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(new TinyDiagnostic(
                TinyDiagnosticSeverity.Warning,
                $"Could not inspect NuGet assets from '{assetsPath}': {ex.Message}",
                projectPath,
                1,
                1,
                Code: TinyDiagnosticCodes.ProjectAssetsUnavailable));
        }

        return new TinyMetadataReferenceLoadResult(
            Deduplicate(references),
            diagnostics);
    }

    private static HashSet<string> ReadDirectPackageIds(string projectPath)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(projectPath))
        {
            return ids;
        }

        var document = XDocument.Load(projectPath);

        foreach (var element in document
                     .Descendants()
                     .Where(element =>
                         element.Name.LocalName == "PackageReference"))
        {
            var packageId =
                element.Attribute("Include")?.Value?.Trim() ??
                element.Attribute("Update")?.Value?.Trim();

            if (!string.IsNullOrWhiteSpace(packageId))
            {
                ids.Add(packageId);
            }
        }

        return ids;
    }

    private static IEnumerable<TinyMetadataReferenceInfo> ReadHintPathReferences(
        string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            yield break;
        }

        var document = XDocument.Load(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;

        foreach (var reference in document
                     .Descendants()
                     .Where(element =>
                         element.Name.LocalName == "Reference"))
        {
            var hintPath = reference
                .Elements()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "HintPath")
                ?.Value
                ?.Trim();

            if (string.IsNullOrWhiteSpace(hintPath))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(
                Path.Combine(projectDirectory, hintPath));

            if (!File.Exists(fullPath))
            {
                continue;
            }

            var assemblyName = TryReadAssemblyName(fullPath);

            if (assemblyName is null)
            {
                continue;
            }

            yield return new TinyMetadataReferenceInfo(
                fullPath,
                assemblyName,
                true,
                Path.GetFileName(fullPath));
        }
    }

    private static JsonProperty? SelectTarget(JsonElement targets)
    {
        JsonProperty? fallback = null;

        foreach (var property in targets.EnumerateObject())
        {
            fallback ??= property;

            if (!property.Name.Contains('/'))
            {
                return property;
            }
        }

        return fallback;
    }

    private static string? ResolvePackageAsset(
        IReadOnlyList<string> packageRoots,
        string libraryPath,
        string assetPath)
    {
        foreach (var packageRoot in packageRoots)
        {
            var candidate = Path.GetFullPath(
                Path.Combine(
                    packageRoot,
                    libraryPath.Replace('/', Path.DirectorySeparatorChar),
                    assetPath.Replace('/', Path.DirectorySeparatorChar)));

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? TryReadAssemblyName(string assemblyPath)
    {
        try
        {
            return AssemblyName.GetAssemblyName(assemblyPath).Name;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<TinyMetadataReferenceInfo> Deduplicate(
        IEnumerable<TinyMetadataReferenceInfo> references)
    {
        return references
            .GroupBy(
                reference => reference.Path,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(reference => reference.IsDirect)
                .First())
            .OrderBy(reference => reference.Path, StringComparer.Ordinal)
            .ToArray();
    }
}
