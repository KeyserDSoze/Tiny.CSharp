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
    public TinyProperty(string name, string type, int mode)
        : this(name, new TinyType(type, Array.Empty<TinyType>()), mode)
    {
    }

    public TinyProperty(string name, TinyType type, int mode)
    {
        Name = name;
        Type = type;
        Mode = mode;
    }

    public string Name { get; }
    public TinyType Type { get; }
    public int Mode { get; }
}
