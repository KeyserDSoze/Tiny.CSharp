using System.Text;

namespace TinyCSharp.Compiler.Language;

public sealed record TinyType(
    string Name,
    IReadOnlyList<TinyType> TypeArguments,
    bool IsNullable = false,
    int ArrayDepth = 0,
    string? ResolvedNamespace = null)
{
    public static TinyType String { get; } = new("string", Array.Empty<TinyType>());

    public bool IsDefaultString =>
        string.Equals(Name, "string", StringComparison.Ordinal) &&
        TypeArguments.Count == 0 &&
        !IsNullable &&
        ArrayDepth == 0;

    public bool ContainsName(string name)
    {
        if (string.Equals(Name, name, StringComparison.Ordinal))
        {
            return true;
        }

        return TypeArguments.Any(argument => argument.ContainsName(name));
    }

    public string ToCSharp()
    {
        var sb = new StringBuilder(Name);

        if (TypeArguments.Count > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(",", TypeArguments.Select(argument => argument.ToCSharp())));
            sb.Append('>');
        }

        if (IsNullable)
        {
            sb.Append('?');
        }

        for (var i = 0; i < ArrayDepth; i++)
        {
            sb.Append("[]");
        }

        return sb.ToString();
    }

    public string ToTiny()
    {
        var sb = new StringBuilder(TinyLanguage.GetCanonicalTypeToken(Name));

        if (TypeArguments.Count > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(",", TypeArguments.Select(argument => argument.ToTiny())));
            sb.Append('>');
        }

        if (IsNullable)
        {
            sb.Append('?');
        }

        for (var i = 0; i < ArrayDepth; i++)
        {
            sb.Append("[]");
        }

        return sb.ToString();
    }

    public static bool TryParseTiny(string token, out TinyType type)
    {
        var parser = new TinyTypeParser(token);
        return parser.TryParse(out type);
    }

    private sealed class TinyTypeParser
    {
        private readonly string _text;
        private int _position;

        public TinyTypeParser(string text)
        {
            _text = text;
        }

        public bool TryParse(out TinyType type)
        {
            type = String;

            if (!TryParseType(out var parsed))
            {
                return false;
            }

            if (_position != _text.Length)
            {
                return false;
            }

            type = parsed;
            return true;
        }

        private bool TryParseType(out TinyType type)
        {
            type = String;

            if (!TryParseName(out var rawName))
            {
                return false;
            }

            var name = TinyLanguage.TryExpandTypeAlias(rawName, out var expanded)
                ? expanded
                : rawName;

            var arguments = new List<TinyType>();

            if (Match('<'))
            {
                do
                {
                    if (!TryParseType(out var argument))
                    {
                        return false;
                    }

                    arguments.Add(argument);
                }
                while (Match(','));

                if (!Match('>'))
                {
                    return false;
                }
            }

            var nullable = Match('?');
            var arrayDepth = 0;

            while (Match('['))
            {
                if (!Match(']'))
                {
                    return false;
                }

                arrayDepth++;
            }

            type = new TinyType(name, arguments, nullable, arrayDepth);
            return true;
        }

        private bool TryParseName(out string name)
        {
            var start = _position;

            if (_position >= _text.Length ||
                !(char.IsLetter(_text[_position]) || _text[_position] == '_'))
            {
                name = string.Empty;
                return false;
            }

            _position++;

            while (_position < _text.Length)
            {
                var ch = _text[_position];
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')
                {
                    _position++;
                    continue;
                }

                break;
            }

            name = _text.Substring(start, _position - start);
            return true;
        }

        private bool Match(char value)
        {
            if (_position >= _text.Length || _text[_position] != value)
            {
                return false;
            }

            _position++;
            return true;
        }
    }
}
