namespace TinyCSharp.Compiler.Language;

public class TinyDocument
{
    public string SourceFilePath { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public TinyTypeDeclaration TypeDeclaration { get; set; } = TinyTypeDeclaration.PublicSealedClass;
    public string ClassName { get; set; } = string.Empty;
    public List<TinyProperty> Properties { get; } = new();
    public List<string> Usings { get; } = new();
}

public sealed class TinyProperty
{
    public TinyProperty(
        string name,
        string type,
        int mode,
        int typeLine = 1,
        int typeColumn = 1)
        : this(
            name,
            new TinyType(type, Array.Empty<TinyType>()),
            mode,
            typeLine,
            typeColumn)
    {
    }

    public TinyProperty(
        string name,
        TinyType type,
        int mode,
        int typeLine = 1,
        int typeColumn = 1)
    {
        Name = name;
        Type = type;
        Mode = mode;
        TypeLine = typeLine;
        TypeColumn = typeColumn;
    }

    public string Name { get; }
    public TinyType Type { get; set; }
    public int Mode { get; }
    public int TypeLine { get; }
    public int TypeColumn { get; }
}
