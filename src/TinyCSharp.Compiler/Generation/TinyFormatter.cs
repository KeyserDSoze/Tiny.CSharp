using System.Text;
using TinyCSharp.Compiler.Language;

namespace TinyCSharp.Compiler.Generation;

public sealed class TinyFormatter
{
    public string Format(TinyDocument document)
    {
        var sb = new StringBuilder();
        var hasDirectives = false;

        if (!string.IsNullOrWhiteSpace(document.Namespace))
        {
            sb.Append("n:").AppendLine(document.Namespace);
            hasDirectives = true;
        }

        foreach (var @using in document.Usings
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(value => value, StringComparer.Ordinal))
        {
            sb.Append("u:").AppendLine(@using);
            hasDirectives = true;
        }

        if (hasDirectives)
        {
            sb.AppendLine();
        }

        sb.Append(document.TypeDeclaration.ToToken())
            .Append(' ')
            .Append(document.ClassName)
            .Append(" => ");

        sb.Append(string.Join(",", document.Properties.Select(FormatProperty)));
        sb.AppendLine();

        return sb.ToString();
    }

    private static string FormatProperty(TinyProperty property)
    {
        var sb = new StringBuilder(property.Name);

        if (!property.Type.IsDefaultString)
        {
            sb.Append(':').Append(property.Type.ToTiny());
        }

        if (property.Mode != 0)
        {
            sb.Append('|').Append(property.Mode);
        }

        return sb.ToString();
    }
}
