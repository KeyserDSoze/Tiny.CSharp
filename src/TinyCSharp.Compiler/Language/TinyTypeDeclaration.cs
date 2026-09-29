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
    bool IsAbstract,
    bool IsSealed,
    bool IsPartial,
    TinyTypeKind Kind)
{
    public static TinyTypeDeclaration PublicSealedClass { get; } =
        new(
            TinyAccessibility.Public,
            IsAbstract: false,
            IsSealed: true,
            IsPartial: false,
            TinyTypeKind.Class);

    public static bool TryParse(
        string token,
        out TinyTypeDeclaration declaration)
    {
        declaration = PublicSealedClass;

        if (string.IsNullOrWhiteSpace(token) ||
            token.Length < 2)
        {
            return false;
        }

        var accessibility = token[0] switch
        {
            'p' => TinyAccessibility.Public,
            'i' => TinyAccessibility.Internal,
            _ => (TinyAccessibility?)null
        };

        if (accessibility is null ||
            token[^1] != 'c')
        {
            return false;
        }

        var modifierToken = token.Substring(
            1,
            token.Length - 2);

        if (!TryParseModifiers(
                modifierToken,
                out var isAbstract,
                out var isSealed,
                out var isPartial))
        {
            return false;
        }

        declaration = new TinyTypeDeclaration(
            accessibility.Value,
            isAbstract,
            isSealed,
            isPartial,
            TinyTypeKind.Class);

        return true;
    }

    public string ToToken()
    {
        var accessibility = Accessibility switch
        {
            TinyAccessibility.Public => "p",
            TinyAccessibility.Internal => "i",
            _ => throw new InvalidOperationException(
                $"Unsupported accessibility: {Accessibility}")
        };

        var modifiers = string.Empty;

        if (IsAbstract)
        {
            modifiers += "a";
        }
        else if (IsSealed)
        {
            modifiers += "s";
        }

        if (IsPartial)
        {
            modifiers += "p";
        }

        var kind = Kind switch
        {
            TinyTypeKind.Class => "c",
            _ => throw new InvalidOperationException(
                $"Unsupported type kind: {Kind}")
        };

        return accessibility + modifiers + kind;
    }

    private static bool TryParseModifiers(
        string token,
        out bool isAbstract,
        out bool isSealed,
        out bool isPartial)
    {
        isAbstract = false;
        isSealed = false;
        isPartial = false;

        var position = 0;

        if (position < token.Length &&
            token[position] == 'a')
        {
            isAbstract = true;
            position++;
        }
        else if (position < token.Length &&
                 token[position] == 's')
        {
            isSealed = true;
            position++;
        }

        if (position < token.Length &&
            token[position] == 'p')
        {
            isPartial = true;
            position++;
        }

        return position == token.Length;
    }
}
