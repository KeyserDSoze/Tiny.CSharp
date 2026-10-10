namespace TinyCSharp.Compiler.Language;

public enum TinyAccessibility
{
    Public,
    Internal
}

public enum TinyTypeKind
{
    Class,
    Record
}

public sealed record TinyTypeDeclaration(
    TinyAccessibility Accessibility,
    bool IsAbstract,
    bool IsSealed,
    bool IsPartial,
    TinyTypeKind Kind)
{
    public bool IsStatic { get; init; }

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
            !TryParseKind(
                token[^1],
                out var kind))
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
                out var isStatic,
                out var isPartial))
        {
            return false;
        }

        var parsed = new TinyTypeDeclaration(
            accessibility.Value,
            isAbstract,
            isSealed,
            isPartial,
            kind)
        {
            IsStatic = isStatic
        };

        if (!parsed.IsValidCombination())
        {
            return false;
        }

        declaration = parsed;
        return true;
    }

    public string ToToken()
    {
        if (!IsValidCombination())
        {
            throw new InvalidOperationException(
                "The Tiny.CSharp type declaration contains an invalid modifier/kind combination.");
        }

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
        else if (IsStatic)
        {
            modifiers += "t";
        }

        if (IsPartial)
        {
            modifiers += "p";
        }

        var kind = Kind switch
        {
            TinyTypeKind.Class => "c",
            TinyTypeKind.Record => "r",
            _ => throw new InvalidOperationException(
                $"Unsupported type kind: {Kind}")
        };

        return accessibility + modifiers + kind;
    }

    public string ToCSharpDeclarationPrefix()
    {
        if (!IsValidCombination())
        {
            throw new InvalidOperationException(
                "The Tiny.CSharp type declaration contains an invalid modifier/kind combination.");
        }

        var parts = new List<string>
        {
            Accessibility switch
            {
                TinyAccessibility.Public => "public",
                TinyAccessibility.Internal => "internal",
                _ => throw new InvalidOperationException(
                    $"Unsupported accessibility: {Accessibility}")
            }
        };

        if (IsAbstract)
        {
            parts.Add("abstract");
        }
        else if (IsSealed)
        {
            parts.Add("sealed");
        }
        else if (IsStatic)
        {
            parts.Add("static");
        }

        if (IsPartial)
        {
            parts.Add("partial");
        }

        parts.Add(Kind switch
        {
            TinyTypeKind.Class => "class",
            TinyTypeKind.Record => "record",
            _ => throw new InvalidOperationException(
                $"Unsupported type kind: {Kind}")
        });

        return string.Join(' ', parts);
    }

    public bool IsValidCombination()
    {
        var primaryModifierCount =
            (IsAbstract ? 1 : 0) +
            (IsSealed ? 1 : 0) +
            (IsStatic ? 1 : 0);

        if (primaryModifierCount > 1)
        {
            return false;
        }

        return Kind switch
        {
            TinyTypeKind.Class => true,
            TinyTypeKind.Record => !IsStatic,
            _ => false
        };
    }

    private static bool TryParseKind(
        char token,
        out TinyTypeKind kind)
    {
        switch (token)
        {
            case 'c':
                kind = TinyTypeKind.Class;
                return true;
            case 'r':
                kind = TinyTypeKind.Record;
                return true;
            default:
                kind = TinyTypeKind.Class;
                return false;
        }
    }

    private static bool TryParseModifiers(
        string token,
        out bool isAbstract,
        out bool isSealed,
        out bool isStatic,
        out bool isPartial)
    {
        isAbstract = false;
        isSealed = false;
        isStatic = false;
        isPartial = false;

        var position = 0;

        if (position < token.Length)
        {
            switch (token[position])
            {
                case 'a':
                    isAbstract = true;
                    position++;
                    break;
                case 's':
                    isSealed = true;
                    position++;
                    break;
                case 't':
                    isStatic = true;
                    position++;
                    break;
            }
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
