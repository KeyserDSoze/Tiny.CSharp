using Microsoft.CodeAnalysis.CSharp;
using TinyCSharp.Compiler.Compilation;
using TinyCSharp.Compiler.Diagnostics;
using TinyCSharp.Compiler.Language;

namespace TinyCSharp.Compiler.Parsing;

public sealed class TinyParser
{
    private string _content = string.Empty;
    private string _sourceFilePath = string.Empty;
    private int _position;
    private List<TinyDiagnostic> _diagnostics = new();

    public TinySyntaxTree Parse(string content, string sourceFilePath = "")
    {
        _content = content;
        _sourceFilePath = sourceFilePath;
        _position = 0;
        _diagnostics = new List<TinyDiagnostic>();

        var syntaxTree = new TinySyntaxTree
        {
            SourceFilePath = sourceFilePath
        };

        SkipWhitespaceAndComments();

        if (Match("n:"))
        {
            var namespacePosition = _position;
            var namespaceName = ParseLineValue();

            if (!IsValidNamespaceName(namespaceName))
            {
                AddError(
                    TinyDiagnosticCodes.InvalidNamespace,
                    $"Invalid namespace '{namespaceName}'.",
                    namespacePosition);
                return Invalid(syntaxTree);
            }

            syntaxTree.Namespace = namespaceName;
            SkipWhitespaceAndComments();
        }

        while (Match("u:"))
        {
            var usingPosition = _position;
            var usingName = ParseLineValue();

            if (!IsValidNamespaceName(usingName))
            {
                AddError(
                    TinyDiagnosticCodes.InvalidUsing,
                    $"Invalid using namespace '{usingName}'.",
                    usingPosition);
                return Invalid(syntaxTree);
            }

            if (!syntaxTree.Usings.Contains(
                    usingName,
                    StringComparer.Ordinal))
            {
                syntaxTree.Usings.Add(usingName);
                var (usingLine, usingColumn) =
                    GetLineColumn(usingPosition);
                syntaxTree.UsingLocations[usingName] =
                    new TinySourceLocation(
                        usingLine,
                        usingColumn);
            }

            SkipWhitespaceAndComments();
        }

        if (Match("n:"))
        {
            AddError(
                TinyDiagnosticCodes.InvalidNamespaceDirectiveOrder,
                "The namespace directive may appear only once and before all using directives.",
                _position - 2);
            return Invalid(syntaxTree);
        }

        var declarationPosition = _position;
        var declarationToken = ParseTypeDeclarationToken();
        if (!TinyTypeDeclaration.TryParse(declarationToken, out var typeDeclaration))
        {
            AddError(
                TinyDiagnosticCodes.UnsupportedTypeDeclaration,
                $"Unsupported type declaration '{declarationToken}'. Expected pc, psc, ic, or isc.",
                declarationPosition);
            return Invalid(syntaxTree);
        }

        syntaxTree.TypeDeclaration = typeDeclaration;
        SkipWhitespaceAndComments();

        var className = ParseIdentifier();
        if (string.IsNullOrEmpty(className))
        {
            return Invalid(syntaxTree);
        }

        syntaxTree.ClassName = className;
        SkipWhitespaceAndComments();

        if (!Match("=>"))
        {
            AddError(
                TinyDiagnosticCodes.ExpectedClassArrow,
                "Expected '=>' after class name.",
                _position);
            return Invalid(syntaxTree);
        }

        SkipWhitespaceAndComments();

        while (!IsEndOfContent())
        {
            var propertyPosition = _position;
            var property = ParseProperty();
            if (property is null)
            {
                return Invalid(syntaxTree);
            }

            if (syntaxTree.Properties.Any(existing =>
                    string.Equals(
                        existing.Name,
                        property.Name,
                        StringComparison.Ordinal)))
            {
                AddError(
                    TinyDiagnosticCodes.DuplicateProperty,
                    $"Property '{property.Name}' is declared more than once.",
                    propertyPosition);
                return Invalid(syntaxTree);
            }

            syntaxTree.Properties.Add(property);
            SkipWhitespaceAndComments();

            if (Match(","))
            {
                SkipWhitespaceAndComments();
                continue;
            }

            if (IsEndOfContent())
            {
                break;
            }

            AddError(
                TinyDiagnosticCodes.ExpectedPropertySeparator,
                "Expected ',' or end of content after property declaration.",
                _position);
            return Invalid(syntaxTree);
        }

        syntaxTree.IsValid = _diagnostics.Count == 0;
        syntaxTree.Diagnostics = _diagnostics;
        return syntaxTree;
    }

    private TinySyntaxTree Invalid(TinySyntaxTree tree)
    {
        tree.IsValid = false;
        tree.Diagnostics = _diagnostics;
        return tree;
    }

    private TinyProperty? ParseProperty()
    {
        var propertyName = ParseIdentifier();
        if (string.IsNullOrEmpty(propertyName))
        {
            return null;
        }

        var type = TinyType.String;
        var mode = 0;
        var typePosition = _position;

        if (Match(":"))
        {
            typePosition = _position;
            var typeToken = ParseTypeToken();

            if (string.IsNullOrWhiteSpace(typeToken))
            {
                AddError(
                    TinyDiagnosticCodes.ExpectedPropertyType,
                    "Expected type after ':'.",
                    typePosition);
                return null;
            }

            if (!TinyType.TryParseTiny(typeToken, out type))
            {
                AddError(
                    TinyDiagnosticCodes.InvalidTypeSyntax,
                    $"Invalid Tiny.CSharp type syntax '{typeToken}'.",
                    typePosition);
                return null;
            }
        }

        if (Match("|"))
        {
            var modePosition = _position;
            var modeStart = _position;

            while (_position < _content.Length && char.IsDigit(_content[_position]))
            {
                _position++;
            }

            var modeToken = _content.Substring(modeStart, _position - modeStart);
            if (!int.TryParse(modeToken, out mode))
            {
                AddError(
                    TinyDiagnosticCodes.ExpectedAccessorMode,
                    "Expected numeric accessor mode after '|'.",
                    modePosition);
                return null;
            }

            if (mode is < 0 or > 2)
            {
                AddError(
                    TinyDiagnosticCodes.InvalidAccessorMode,
                    "Accessor mode must be 0, 1, or 2.",
                    modePosition);
                return null;
            }
        }

        var (typeLine, typeColumn) = GetLineColumn(typePosition);
        return new TinyProperty(
            propertyName,
            type,
            mode,
            typeLine,
            typeColumn);
    }

    private string ParseTypeToken()
    {
        var start = _position;
        var genericDepth = 0;

        while (_position < _content.Length)
        {
            var ch = _content[_position];

            if (ch == '<')
            {
                genericDepth++;
                _position++;
                continue;
            }

            if (ch == '>')
            {
                if (genericDepth == 0)
                {
                    break;
                }

                genericDepth--;
                _position++;
                continue;
            }

            if (genericDepth == 0 &&
                (ch == '|' || ch == ',' || char.IsWhiteSpace(ch)))
            {
                break;
            }

            _position++;
        }

        return _content.Substring(start, _position - start);
    }

    private string ParseTypeDeclarationToken()
    {
        var start = _position;

        while (_position < _content.Length && !char.IsWhiteSpace(_content[_position]))
        {
            if (_content[_position] == '/' &&
                _position + 1 < _content.Length &&
                _content[_position + 1] == '/')
            {
                break;
            }

            _position++;
        }

        return _content.Substring(start, _position - start);
    }

    private string ParseIdentifier()
    {
        var start = _position;

        if (_position >= _content.Length ||
            !(char.IsLetter(_content[_position]) || _content[_position] == '_'))
        {
            AddError(
                TinyDiagnosticCodes.ExpectedIdentifier,
                "Expected identifier.",
                _position);
            return string.Empty;
        }

        _position++;

        while (_position < _content.Length &&
               (char.IsLetterOrDigit(_content[_position]) || _content[_position] == '_'))
        {
            _position++;
        }

        return _content.Substring(start, _position - start);
    }

    private static bool IsValidNamespaceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segments = value.Split('.');

        if (segments.Length == 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return false;
            }

            if (segment[0] == '@')
            {
                var escaped = segment.Substring(1);

                if (string.IsNullOrWhiteSpace(escaped) ||
                    (!SyntaxFacts.IsValidIdentifier(escaped) &&
                     SyntaxFacts.GetKeywordKind(escaped) == SyntaxKind.None))
                {
                    return false;
                }

                continue;
            }

            if (!SyntaxFacts.IsValidIdentifier(segment) ||
                SyntaxFacts.GetKeywordKind(segment) != SyntaxKind.None)
            {
                return false;
            }
        }

        return true;
    }

    private string ParseLineValue()
    {
        var start = _position;

        while (_position < _content.Length &&
               _content[_position] != '\n' &&
               _content[_position] != '\r')
        {
            _position++;
        }

        var value = _content.Substring(start, _position - start).Trim();
        var commentIndex = value.IndexOf("//", StringComparison.Ordinal);

        if (commentIndex >= 0)
        {
            value = value.Substring(0, commentIndex).TrimEnd();
        }

        if (_position < _content.Length && _content[_position] == '\r')
        {
            _position++;
        }

        if (_position < _content.Length && _content[_position] == '\n')
        {
            _position++;
        }

        return value;
    }

    private void SkipWhitespaceAndComments()
    {
        while (_position < _content.Length)
        {
            if (char.IsWhiteSpace(_content[_position]))
            {
                _position++;
                continue;
            }

            if (_content[_position] == '/' &&
                _position + 1 < _content.Length &&
                _content[_position + 1] == '/')
            {
                _position += 2;

                while (_position < _content.Length &&
                       _content[_position] != '\n' &&
                       _content[_position] != '\r')
                {
                    _position++;
                }

                continue;
            }

            break;
        }
    }

    private bool Match(string token)
    {
        if (_position + token.Length > _content.Length)
        {
            return false;
        }

        if (!_content.AsSpan(_position, token.Length).SequenceEqual(token))
        {
            return false;
        }

        _position += token.Length;
        return true;
    }

    private void AddError(string code, string message, int position)
    {
        var (line, column) = GetLineColumn(position);
        _diagnostics.Add(new TinyDiagnostic(
            TinyDiagnosticSeverity.Error,
            message,
            _sourceFilePath,
            line,
            column,
            Code: code));
    }

    private (int Line, int Column) GetLineColumn(int position)
    {
        var line = 1;
        var column = 1;
        var limit = Math.Min(position, _content.Length);

        for (var i = 0; i < limit; i++)
        {
            if (_content[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private bool IsEndOfContent() => _position >= _content.Length;
}

public sealed class TinySyntaxTree : TinyDocument
{
    public bool IsValid { get; set; } = true;
    public List<TinyDiagnostic> Diagnostics { get; set; } = new();
    public Dictionary<string, TinySourceLocation> UsingLocations { get; } =
        new(StringComparer.Ordinal);
}

public sealed record TinySourceLocation(
    int Line,
    int Column);
