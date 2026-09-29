using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TinyCSharp.Compiler.Diagnostics;
using TinyCSharp.Compiler.Language;

namespace TinyCSharp.Compiler.Decompilation;

public sealed class CSharpDecompiler
{
    public TinyDecompilationResult Decompile(string source, string sourceFilePath = "")
    {
        var diagnostics = new List<TinyDecompilationDiagnostic>();
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        foreach (var diagnostic in syntaxTree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            var span = diagnostic.Location.GetLineSpan();
            diagnostics.Add(new TinyDecompilationDiagnostic(
                TinyDiagnosticCodes.InvalidCSharpSyntax,
                diagnostic.GetMessage(),
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1));
        }

        if (diagnostics.Count > 0)
        {
            return new TinyDecompilationResult(null, diagnostics);
        }

        var root = syntaxTree.GetCompilationUnitRoot();
        var document = new TinyDocument
        {
            SourceFilePath = sourceFilePath
        };

        if (!TryReadUsings(root.Usings, document, diagnostics))
        {
            return new TinyDecompilationResult(null, diagnostics);
        }

        IReadOnlyList<MemberDeclarationSyntax> members = root.Members;

        if (root.Members.Count == 1 &&
            root.Members[0] is BaseNamespaceDeclarationSyntax namespaceDeclaration)
        {
            document.Namespace = namespaceDeclaration.Name.ToString();

            if (!TryReadUsings(namespaceDeclaration.Usings, document, diagnostics))
            {
                return new TinyDecompilationResult(null, diagnostics);
            }

            members = namespaceDeclaration.Members;
        }
        else if (root.Members.OfType<BaseNamespaceDeclarationSyntax>().Any())
        {
            AddUnsupported(
                diagnostics,
                root,
                "The current Tiny.CSharp profile supports at most one namespace containing one class.");
            return new TinyDecompilationResult(null, diagnostics);
        }

        if (members.Count != 1 || members[0] is not ClassDeclarationSyntax classDeclaration)
        {
            AddUnsupported(
                diagnostics,
                root,
                "The current Tiny.CSharp profile requires exactly one top-level class.");
            return new TinyDecompilationResult(null, diagnostics);
        }

        if (!TryReadClass(classDeclaration, document, diagnostics))
        {
            return new TinyDecompilationResult(null, diagnostics);
        }

        return new TinyDecompilationResult(document, diagnostics);
    }

    private static bool TryReadUsings(
        SyntaxList<UsingDirectiveSyntax> usings,
        TinyDocument document,
        List<TinyDecompilationDiagnostic> diagnostics)
    {
        foreach (var directive in usings)
        {
            if (directive.Alias is not null ||
                directive.StaticKeyword != default ||
                directive.GlobalKeyword != default ||
                directive.Name is null)
            {
                AddUnsupported(
                    diagnostics,
                    directive,
                    "Using aliases, global using directives, and static using directives are not supported.");
                return false;
            }

            document.Usings.Add(directive.Name.ToString());
        }

        return true;
    }

    private static bool TryReadClass(
        ClassDeclarationSyntax classDeclaration,
        TinyDocument document,
        List<TinyDecompilationDiagnostic> diagnostics)
    {
        if (classDeclaration.AttributeLists.Count > 0 ||
            classDeclaration.TypeParameterList is not null ||
            classDeclaration.BaseList is not null ||
            classDeclaration.ConstraintClauses.Count > 0)
        {
            AddUnsupported(
                diagnostics,
                classDeclaration,
                "Attributes, generic parameters, base types, interfaces, and generic constraints are not supported yet.");
            return false;
        }

        var modifiers = classDeclaration.Modifiers;
        var isPublic = modifiers.Any(SyntaxKind.PublicKeyword);
        var isInternal = modifiers.Any(SyntaxKind.InternalKeyword);

        if (isPublic && isInternal)
        {
            AddUnsupported(diagnostics, classDeclaration, "A class cannot be both public and internal.");
            return false;
        }

        foreach (var modifier in modifiers)
        {
            if (!modifier.IsKind(SyntaxKind.PublicKeyword) &&
                !modifier.IsKind(SyntaxKind.InternalKeyword) &&
                !modifier.IsKind(SyntaxKind.SealedKeyword))
            {
                AddUnsupported(
                    diagnostics,
                    modifier,
                    $"Class modifier '{modifier.Text}' is not supported by the current Tiny.CSharp profile.");
                return false;
            }
        }

        document.TypeDeclaration = new TinyTypeDeclaration(
            isPublic ? TinyAccessibility.Public : TinyAccessibility.Internal,
            modifiers.Any(SyntaxKind.SealedKeyword),
            TinyTypeKind.Class);
        document.ClassName = classDeclaration.Identifier.ValueText;

        foreach (var member in classDeclaration.Members)
        {
            if (member is not PropertyDeclarationSyntax propertyDeclaration)
            {
                AddUnsupported(
                    diagnostics,
                    member,
                    $"Member kind '{member.Kind()}' is not supported by the current Tiny.CSharp profile.");
                return false;
            }

            if (!TryReadProperty(propertyDeclaration, out var property, diagnostics))
            {
                return false;
            }

            document.Properties.Add(property);
        }

        return true;
    }

    private static bool TryReadProperty(
        PropertyDeclarationSyntax declaration,
        out TinyProperty property,
        List<TinyDecompilationDiagnostic> diagnostics)
    {
        property = null!;

        if (declaration.AttributeLists.Count > 0 ||
            declaration.ExplicitInterfaceSpecifier is not null ||
            declaration.ExpressionBody is not null)
        {
            AddUnsupported(
                diagnostics,
                declaration,
                "Attributed, explicit-interface, and expression-bodied properties are not supported yet.");
            return false;
        }

        if (declaration.Modifiers.Count != 1 ||
            !declaration.Modifiers[0].IsKind(SyntaxKind.PublicKeyword))
        {
            AddUnsupported(
                diagnostics,
                declaration,
                "The current Tiny.CSharp property profile supports public instance properties only.");
            return false;
        }

        if (!TryReadType(declaration.Type, out var type))
        {
            AddUnsupported(
                diagnostics,
                declaration.Type,
                $"Property type '{declaration.Type}' is not representable by the current Tiny.CSharp type grammar.");
            return false;
        }

        if (declaration.AccessorList is null ||
            declaration.AccessorList.Accessors.Count != 2)
        {
            AddUnsupported(
                diagnostics,
                declaration,
                "Properties must use one of: get/set, get/init, or get/private set.");
            return false;
        }

        var getter = declaration.AccessorList.Accessors.SingleOrDefault(
            accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
        var setter = declaration.AccessorList.Accessors.SingleOrDefault(
            accessor => accessor.IsKind(SyntaxKind.SetAccessorDeclaration) ||
                        accessor.IsKind(SyntaxKind.InitAccessorDeclaration));

        if (getter is null || setter is null ||
            getter.Body is not null ||
            getter.ExpressionBody is not null ||
            getter.Modifiers.Count != 0 ||
            setter.Body is not null ||
            setter.ExpressionBody is not null)
        {
            AddUnsupported(
                diagnostics,
                declaration,
                "Only auto-properties with supported accessor modes can be represented.");
            return false;
        }

        int mode;

        if (setter.IsKind(SyntaxKind.InitAccessorDeclaration) && setter.Modifiers.Count == 0)
        {
            mode = 1;
        }
        else if (setter.IsKind(SyntaxKind.SetAccessorDeclaration) && setter.Modifiers.Count == 0)
        {
            mode = 0;
        }
        else if (setter.IsKind(SyntaxKind.SetAccessorDeclaration) &&
                 setter.Modifiers.Count == 1 &&
                 setter.Modifiers[0].IsKind(SyntaxKind.PrivateKeyword))
        {
            mode = 2;
        }
        else
        {
            AddUnsupported(
                diagnostics,
                setter,
                "Only set, init, and private set accessors are supported.");
            return false;
        }

        if (!ValidateInitializer(declaration, type, mode, diagnostics))
        {
            return false;
        }

        property = new TinyProperty(declaration.Identifier.ValueText, type, mode);
        return true;
    }

    private static bool TryReadType(TypeSyntax syntax, out TinyType type)
    {
        type = TinyType.String;

        switch (syntax)
        {
            case PredefinedTypeSyntax predefined:
            {
                var name = predefined.Keyword.ValueText;
                if (!TinyLanguage.TryExpandTypeAlias(
                        TinyLanguage.GetCanonicalTypeToken(name),
                        out var expanded) ||
                    !string.Equals(expanded, name, StringComparison.Ordinal))
                {
                    return false;
                }

                type = new TinyType(name, Array.Empty<TinyType>());
                return true;
            }

            case IdentifierNameSyntax identifier:
                type = new TinyType(identifier.Identifier.ValueText, Array.Empty<TinyType>());
                return true;

            case GenericNameSyntax generic:
            {
                var arguments = new List<TinyType>();

                foreach (var argumentSyntax in generic.TypeArgumentList.Arguments)
                {
                    if (!TryReadType(argumentSyntax, out var argument))
                    {
                        return false;
                    }

                    arguments.Add(argument);
                }

                type = new TinyType(generic.Identifier.ValueText, arguments);
                return true;
            }

            case NullableTypeSyntax nullable:
                if (!TryReadType(nullable.ElementType, out var nullableElement) ||
                    nullableElement.ArrayDepth > 0)
                {
                    return false;
                }

                type = nullableElement with { IsNullable = true };
                return true;

            case ArrayTypeSyntax array:
                if (!TryReadType(array.ElementType, out var element))
                {
                    return false;
                }

                if (array.RankSpecifiers.Any(rank => rank.Rank != 1))
                {
                    return false;
                }

                type = element with { ArrayDepth = element.ArrayDepth + array.RankSpecifiers.Count };
                return true;

            default:
                return false;
        }
    }

    private static bool ValidateInitializer(
        PropertyDeclarationSyntax declaration,
        TinyType type,
        int mode,
        List<TinyDecompilationDiagnostic> diagnostics)
    {
        if (!type.IsDefaultString)
        {
            if (declaration.Initializer is not null)
            {
                AddUnsupported(
                    diagnostics,
                    declaration.Initializer,
                    "Property initializers are not representable by the current Tiny.CSharp profile.");
                return false;
            }

            return true;
        }

        if (mode != 0)
        {
            if (declaration.Initializer is not null)
            {
                AddUnsupported(
                    diagnostics,
                    declaration.Initializer,
                    "String properties using init or private set cannot currently preserve an initializer.");
                return false;
            }

            return true;
        }

        if (declaration.Initializer is null ||
            !IsEmptyStringInitializer(declaration.Initializer.Value))
        {
            AddUnsupported(
                diagnostics,
                declaration,
                "A string get/set property must initialize to string.Empty or an empty string for safe Tiny.CSharp round-trip.");
            return false;
        }

        return true;
    }

    private static bool IsEmptyStringInitializer(ExpressionSyntax expression)
    {
        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression) &&
            literal.Token.ValueText.Length == 0)
        {
            return true;
        }

        return expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Expression is PredefinedTypeSyntax predefined &&
               predefined.Keyword.IsKind(SyntaxKind.StringKeyword) &&
               memberAccess.Name.Identifier.ValueText == "Empty";
    }

    private static void AddUnsupported(
        List<TinyDecompilationDiagnostic> diagnostics,
        SyntaxNodeOrToken node,
        string message)
    {
        var span = node.GetLocation().GetLineSpan();
        diagnostics.Add(new TinyDecompilationDiagnostic(
            TinyDiagnosticCodes.UnsupportedCSharpConstruct,
            message,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1));
    }
}

public sealed record TinyDecompilationDiagnostic(
    string Code,
    string Message,
    int Line,
    int Column);

public sealed record TinyDecompilationResult(
    TinyDocument? Document,
    IReadOnlyList<TinyDecompilationDiagnostic> Diagnostics)
{
    public bool Success => Document is not null && Diagnostics.Count == 0;
}
