namespace TinyCSharp.Compiler.Diagnostics;

public static class TinyDiagnosticCodes
{
    public const string UnsupportedTypeDeclaration = "TCS1001";
    public const string ExpectedIdentifier = "TCS1002";
    public const string ExpectedClassArrow = "TCS1003";
    public const string ExpectedPropertySeparator = "TCS1004";
    public const string ExpectedPropertyType = "TCS1005";
    public const string ExpectedAccessorMode = "TCS1006";
    public const string InvalidAccessorMode = "TCS1007";
    public const string InvalidTypeSyntax = "TCS1008";
    public const string DuplicateProperty = "TCS1009";

    public const string AmbiguousType = "TCS2001";
    public const string UnresolvedType = "TCS2002";
    public const string InvalidNamespace = "TCS2003";
    public const string InvalidUsing = "TCS2004";
    public const string InvalidNamespaceDirectiveOrder = "TCS2005";
    public const string NamespaceSegmentNormalized = "TCS2006";

    public const string OutputReplacementFailed = "TCS3001";

    public const string ProjectDirectoryUnavailable = "TCS4001";
    public const string FileCompilationFailed = "TCS4002";
    public const string ProjectMetadataUnavailable = "TCS4003";
    public const string ProjectAssetsUnavailable = "TCS4004";

    public const string InternalCompilerFailure = "TCS5001";

    public const string InvalidCSharpSyntax = "TCS6001";
    public const string UnsupportedCSharpConstruct = "TCS6002";
    public const string AmbiguousCSharpType = "TCS6003";
    public const string ProjectDecompilationFailure = "TCS6004";
}
