namespace TinyCSharp.Compiler.Language;

public enum TinyAccessibility
{
    Public,
    Internal
}

public enum TinyTypeKind
{
    Class
}

public sealed record TinyTypeDeclaration(
    TinyAccessibility Accessibility,
    bool IsSealed,
    TinyTypeKind Kind)
{
    public static TinyTypeDeclaration PublicSealedClass { get; } =
        new(TinyAccessibility.Public, true, TinyTypeKind.Class);

    public static bool TryParse(string token, out TinyTypeDeclaration declaration)
    {
        declaration = PublicSealedClass;

        if (string.IsNullOrWhiteSpace(token) || token.Length < 2)
        {
            return false;
        }

        var accessibility = token[0] switch
        {
            'p' => TinyAccessibility.Public,
            'i' => TinyAccessibility.Internal,
            _ => (TinyAccessibility?)null
        };

        if (accessibility is null || token[^1] != 'c')
        {
            return false;
        }

        var modifiers = token.Substring(1, token.Length - 2);
        if (modifiers.Length != 0 && modifiers != "s")
        {
            return false;
        }

        declaration = new TinyTypeDeclaration(
            accessibility.Value,
            modifiers == "s",
            TinyTypeKind.Class);

        return true;
    }

    public string ToToken()
    {
        var accessibility = Accessibility switch
        {
            TinyAccessibility.Public => "p",
            TinyAccessibility.Internal => "i",
            _ => throw new InvalidOperationException($"Unsupported accessibility: {Accessibility}")
        };

        var modifiers = IsSealed ? "s" : string.Empty;

        var kind = Kind switch
        {
            TinyTypeKind.Class => "c",
            _ => throw new InvalidOperationException($"Unsupported type kind: {Kind}")
        };

        return accessibility + modifiers + kind;
    }
}
