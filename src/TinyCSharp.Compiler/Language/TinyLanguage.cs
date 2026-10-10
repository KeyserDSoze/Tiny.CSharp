namespace TinyCSharp.Compiler.Language;

public static class TinyLanguage
{
    private static readonly IReadOnlyDictionary<string, string> AliasToType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["s"] = "string",
            ["i"] = "int",
            ["l"] = "long",
            ["b"] = "bool",
            ["d"] = "double",
            ["m"] = "decimal",
            ["f"] = "float",
            ["c"] = "char",
            ["by"] = "byte",
            ["dt"] = "DateTime",
            ["g"] = "Guid",
            ["o"] = "object"
        };

    private static readonly IReadOnlyDictionary<string, string> TypeToAlias =
        AliasToType.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public static bool TryExpandTypeAlias(string token, out string typeName)
    {
        return AliasToType.TryGetValue(token, out typeName!);
    }

    public static string GetCanonicalTypeToken(string typeName)
    {
        return TypeToAlias.TryGetValue(typeName, out var alias)
            ? alias
            : typeName;
    }
}
