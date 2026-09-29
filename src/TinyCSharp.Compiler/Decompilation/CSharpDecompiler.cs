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
        var semanticPath = string.IsNullOrWhiteSpace(sourceFilePath)
            ? "__tiny_single_file__.cs"
            : sourceFilePath;

        var semanticCompilation = CSharpSemanticCompilation.Create(
            new[] { new CSharpSourceDocument(semanticPath, source) });

        var result = Decompile(semanticCompilation, semanticPath);

        if (result.Document is not null && string.IsNullOrWhiteSpace(sourceFilePath))
        {
            result.Document.SourceFilePath = string.Empty;
        }

        return result;
    }

    public TinyDecompilationResult Decompile(
        CSharpSemanticCompilation semanticCompilation,
        string sourceFilePath)
    {
        var diagnostics = new List<TinyDecompilationDiagnostic>();
        var syntaxTree = semanticCompilation.GetSyntaxTree(sourceFilePath);
        var semanticModel = semanticCompilation.GetSemanticModel(sourceFilePath);

        foreach (var diagnostic in syntaxTree.GetDiagnostics().Where(
                     diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
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

        var allTypeNamesResolved = true;

        if (!TryReadClass(
                classDeclaration,
                semanticModel,
                document,
                diagnostics,
                ref allTypeNamesResolved))
        {
            return new TinyDecompilationResult(null, diagnostics);
        }

        if (allTypeNamesResolved)
        {
            NormalizeUsings(document);
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
        SemanticModel semanticModel,
        TinyDocument document,
        List<TinyDecompilationDiagnostic> diagnostics,
        ref bool allTypeNamesResolved)
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
                !modifier.IsKind(SyntaxKind.AbstractKeyword) &&
                !modifier.IsKind(SyntaxKind.SealedKeyword) &&
                !modifier.IsKind(SyntaxKind.PartialKeyword))
            {
                AddUnsupported(
                    diagnostics,
                    modifier,
                    $"Class modifier '{modifier.Text}' is not supported by the current Tiny.CSharp profile.");
                return false;
            }
        }

        var isAbstract = modifiers.Any(SyntaxKind.AbstractKeyword);
        var isSealed = modifiers.Any(SyntaxKind.SealedKeyword);

        if (isAbstract && isSealed)
        {
            AddUnsupported(
                diagnostics,
                classDeclaration,
                "A class cannot be both abstract and sealed in the current Tiny.CSharp profile.");
            return false;
        }

        document.TypeDeclaration = new TinyTypeDeclaration(
            isPublic ? TinyAccessibility.Public : TinyAccessibility.Internal,
            isAbstract,
            isSealed,
            modifiers.Any(SyntaxKind.PartialKeyword),
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

            if (!TryReadProperty(
                    propertyDeclaration,
                    semanticModel,
                    out var property,
                    diagnostics,
                    out var propertyTypeResolved))
            {
                return false;
            }

            allTypeNamesResolved &= propertyTypeResolved;
            document.Properties.Add(property);
        }

        return true;
    }

    private static bool TryReadProperty(
        PropertyDeclarationSyntax declaration,
        SemanticModel semanticModel,
        out TinyProperty property,
        List<TinyDecompilationDiagnostic> diagnostics,
        out bool typeNamesResolved)
    {
        property = null!;
        typeNamesResolved = true;

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

        if (!TryReadType(
                declaration.Type,
                semanticModel,
                out var type,
                diagnostics,
                out typeNamesResolved))
        {
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

    private static bool TryReadType(
        TypeSyntax syntax,
        SemanticModel semanticModel,
        out TinyType type,
        List<TinyDecompilationDiagnostic> diagnostics,
        out bool allNamesResolved)
    {
        type = TinyType.String;
        allNamesResolved = true;

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
                    AddUnsupported(
                        diagnostics,
                        syntax,
                        $"Predefined type '{syntax}' is not supported by the current Tiny.CSharp profile.");
                    return false;
                }

                type = new TinyType(
                    name,
                    Array.Empty<TinyType>(),
                    ResolvedNamespace: "System");
                return true;
            }

            case IdentifierNameSyntax identifier:
                return TryReadSimpleNamedType(
                    identifier,
                    identifier.Identifier.ValueText,
                    semanticModel,
                    Array.Empty<TinyType>(),
                    diagnostics,
                    out type,
                    out allNamesResolved);

            case GenericNameSyntax generic:
            {
                var arguments = new List<TinyType>();
                var argumentsResolved = true;

                foreach (var argumentSyntax in generic.TypeArgumentList.Arguments)
                {
                    if (!TryReadType(
                            argumentSyntax,
                            semanticModel,
                            out var argument,
                            diagnostics,
                            out var argumentResolved))
                    {
                        return false;
                    }

                    argumentsResolved &= argumentResolved;
                    arguments.Add(argument);
                }

                if (!TryReadSimpleNamedType(
                        generic,
                        generic.Identifier.ValueText,
                        semanticModel,
                        arguments,
                        diagnostics,
                        out type,
                        out var genericResolved))
                {
                    return false;
                }

                allNamesResolved = argumentsResolved && genericResolved;
                return true;
            }

            case QualifiedNameSyntax qualified:
                return TryReadQualifiedType(
                    qualified,
                    semanticModel,
                    diagnostics,
                    out type,
                    out allNamesResolved);

            case NullableTypeSyntax nullable:
                if (!TryReadType(
                        nullable.ElementType,
                        semanticModel,
                        out var nullableElement,
                        diagnostics,
                        out allNamesResolved) ||
                    nullableElement.ArrayDepth > 0)
                {
                    if (nullableElement.ArrayDepth > 0)
                    {
                        AddUnsupported(
                            diagnostics,
                            nullable,
                            "Nullable array references are not representable losslessly by the current Tiny.CSharp type grammar.");
                    }

                    return false;
                }

                type = nullableElement with { IsNullable = true };
                return true;

            case ArrayTypeSyntax array:
                if (!TryReadType(
                        array.ElementType,
                        semanticModel,
                        out var element,
                        diagnostics,
                        out allNamesResolved))
                {
                    return false;
                }

                if (array.RankSpecifiers.Any(rank => rank.Rank != 1))
                {
                    AddUnsupported(
                        diagnostics,
                        array,
                        "Multidimensional arrays are not supported by the current Tiny.CSharp type grammar.");
                    return false;
                }

                type = element with
                {
                    ArrayDepth = element.ArrayDepth + array.RankSpecifiers.Count
                };
                return true;

            default:
                AddUnsupported(
                    diagnostics,
                    syntax,
                    $"Property type '{syntax}' is not representable by the current Tiny.CSharp type grammar.");
                return false;
        }
    }

    private static bool TryReadSimpleNamedType(
        TypeSyntax syntax,
        string sourceName,
        SemanticModel semanticModel,
        IReadOnlyList<TinyType> typeArguments,
        List<TinyDecompilationDiagnostic> diagnostics,
        out TinyType type,
        out bool resolved)
    {
        type = TinyType.String;

        if (!TryResolveNamespace(
                syntax,
                semanticModel,
                diagnostics,
                out var resolvedNamespace,
                out resolved))
        {
            return false;
        }

        type = new TinyType(
            sourceName,
            typeArguments,
            ResolvedNamespace: resolvedNamespace);

        return true;
    }

    private static bool TryReadQualifiedType(
        QualifiedNameSyntax qualified,
        SemanticModel semanticModel,
        List<TinyDecompilationDiagnostic> diagnostics,
        out TinyType type,
        out bool allNamesResolved)
    {
        type = TinyType.String;
        allNamesResolved = true;

        var arguments = new List<TinyType>();

        if (qualified.Right is GenericNameSyntax generic)
        {
            foreach (var argumentSyntax in generic.TypeArgumentList.Arguments)
            {
                if (!TryReadType(
                        argumentSyntax,
                        semanticModel,
                        out var argument,
                        diagnostics,
                        out var argumentResolved))
                {
                    return false;
                }

                allNamesResolved &= argumentResolved;
                arguments.Add(argument);
            }
        }
        else if (qualified.Right is not IdentifierNameSyntax)
        {
            AddUnsupported(
                diagnostics,
                qualified,
                $"Qualified type '{qualified}' is not supported by the current Tiny.CSharp profile.");
            return false;
        }

        if (!TryResolveNamespace(
                qualified,
                semanticModel,
                diagnostics,
                out var resolvedNamespace,
                out var qualifiedResolved))
        {
            return false;
        }

        // A qualified source spelling is itself sufficient to reproduce the type.
        // Semantic resolution is still captured when available so using directives
        // can be normalized without changing binding.
        var rightName = qualified.Right switch
        {
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
            _ => throw new InvalidOperationException()
        };

        type = new TinyType(
            $"{qualified.Left}.{rightName}",
            arguments,
            ResolvedNamespace: resolvedNamespace);

        allNamesResolved &= qualifiedResolved || IsNamespaceQualified(type);
        return true;
    }

    private static bool TryResolveNamespace(
        TypeSyntax syntax,
        SemanticModel semanticModel,
        List<TinyDecompilationDiagnostic> diagnostics,
        out string? namespaceName,
        out bool resolved)
    {
        namespaceName = null;
        resolved = false;

        var typeInfo = semanticModel.GetTypeInfo(syntax);
        if (typeInfo.Type is ITypeSymbol typeSymbol &&
            typeSymbol.TypeKind != TypeKind.Error)
        {
            namespaceName = GetNamespace(typeSymbol);
            resolved = true;
            return true;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(syntax);
        if (symbolInfo.CandidateReason == CandidateReason.Ambiguous)
        {
            var candidates = string.Join(
                ", ",
                symbolInfo.CandidateSymbols.Select(
                    symbol => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));

            AddAmbiguous(
                diagnostics,
                syntax,
                string.IsNullOrWhiteSpace(candidates)
                    ? $"Type '{syntax}' is ambiguous."
                    : $"Type '{syntax}' is ambiguous between: {candidates}.");
            return false;
        }

        // Unresolved project/application types are preserved textually in single-file
        // mode. A multi-file semantic context can resolve them and unlock using
        // canonicalization.
        return true;
    }

    private static string? GetNamespace(ITypeSymbol typeSymbol)
    {
        ITypeSymbol target = typeSymbol;

        while (target is IArrayTypeSymbol array)
        {
            target = array.ElementType;
        }

        if (target is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            target = named.TypeArguments[0];
        }

        var containingNamespace = target.ContainingNamespace;

        return containingNamespace is null || containingNamespace.IsGlobalNamespace
            ? null
            : containingNamespace.ToDisplayString();
    }

    private static void NormalizeUsings(TinyDocument document)
    {
        var requiredNamespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in document.Properties)
        {
            CollectRequiredNamespaces(
                property.Type,
                document.Namespace,
                requiredNamespaces);
        }

        document.Usings.Clear();
        document.Usings.AddRange(requiredNamespaces.OrderBy(
            value => value,
            StringComparer.Ordinal));
    }

    private static void CollectRequiredNamespaces(
        TinyType type,
        string documentNamespace,
        ISet<string> requiredNamespaces)
    {
        if (!string.IsNullOrWhiteSpace(type.ResolvedNamespace) &&
            !string.Equals(
                type.ResolvedNamespace,
                documentNamespace,
                StringComparison.Ordinal) &&
            RequiresExplicitUsing(type))
        {
            requiredNamespaces.Add(type.ResolvedNamespace);
        }

        foreach (var argument in type.TypeArguments)
        {
            CollectRequiredNamespaces(
                argument,
                documentNamespace,
                requiredNamespaces);
        }
    }

    private static bool RequiresExplicitUsing(TinyType type)
    {
        if (TinyLanguage.GetCanonicalTypeToken(type.Name) != type.Name)
        {
            // C# keywords need no import. DateTime and Guid are automatically
            // imported by CSharpGenerator when their Tiny aliases are used.
            return false;
        }

        if (IsNamespaceQualified(type))
        {
            return false;
        }

        return true;
    }

    private static bool IsNamespaceQualified(TinyType type)
    {
        var resolvedNamespace = type.ResolvedNamespace;

        return resolvedNamespace is not null &&
               resolvedNamespace.Length > 0 &&
               type.Name.StartsWith(
                   resolvedNamespace + ".",
                   StringComparison.Ordinal);
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
        AddDiagnostic(
            diagnostics,
            TinyDiagnosticCodes.UnsupportedCSharpConstruct,
            node,
            message);
    }

    private static void AddAmbiguous(
        List<TinyDecompilationDiagnostic> diagnostics,
        SyntaxNodeOrToken node,
        string message)
    {
        AddDiagnostic(
            diagnostics,
            TinyDiagnosticCodes.AmbiguousCSharpType,
            node,
            message);
    }

    private static void AddDiagnostic(
        List<TinyDecompilationDiagnostic> diagnostics,
        string code,
        SyntaxNodeOrToken node,
        string message)
    {
        var location = node.GetLocation();
        var line = 1;
        var column = 1;

        if (location is not null)
        {
            var span = location.GetLineSpan();
            line = span.StartLinePosition.Line + 1;
            column = span.StartLinePosition.Character + 1;
        }

        diagnostics.Add(new TinyDecompilationDiagnostic(
            code,
            message,
            line,
            column));
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
