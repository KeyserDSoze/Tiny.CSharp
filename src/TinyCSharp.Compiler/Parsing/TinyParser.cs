using System;
using System.Collections.Generic;
using System.Text;
using TinyCSharp.Compiler.Compilation;

namespace TinyCSharp.Compiler.Parsing;

public sealed class TinyParser
{
    private string _content;
    private int _position;
    private List<TinyDiagnostic> _diagnostics;
    
    public TinySyntaxTree Parse(string content)
    {
        _content = content;
        _position = 0;
        _diagnostics = new List<TinyDiagnostic>();
        
        var syntaxTree = new TinySyntaxTree();
        
        // Skip whitespace and comments
        SkipWhitespaceAndComments();
        
        // Parse namespace directive if present
        if (Match("n:"))
        {
            var namespaceName = ParseNamespace();
            syntaxTree.Namespace = namespaceName;
            SkipWhitespaceAndComments();
        }
        
        // Parse using directives if present
        while (Match("u:"))
        {
            var usingName = ParseUsing();
            syntaxTree.Usings.Add(usingName);
            SkipWhitespaceAndComments();
        }
        
        // Parse class declaration
        if (!Match("psc"))
        {
            _diagnostics.Add(new TinyDiagnostic(
                TinyDiagnosticSeverity.Error, 
                "Expected 'psc' keyword for public sealed class declaration", 
                "", 0, 0));
            syntaxTree.IsValid = false;
            return syntaxTree;
        }
        
        SkipWhitespaceAndComments();
        
        // Parse class name
        var className = ParseIdentifier();
        syntaxTree.ClassName = className;
        
        SkipWhitespaceAndComments();
        
        // Parse property list
        if (!Match("=>"))
        {
            _diagnostics.Add(new TinyDiagnostic(
                TinyDiagnosticSeverity.Error, 
                "Expected '=>' after class name", 
                "", 0, 0));
            syntaxTree.IsValid = false;
            return syntaxTree;
        }
        
        SkipWhitespaceAndComments();
        
        // Parse properties
        while (!IsEndOfContent())
        {
            var property = ParseProperty();
            if (property == null)
                break;
            
            syntaxTree.Properties.Add(property);
            
            SkipWhitespaceAndComments();
            
            // Check for comma or end
            if (Match(","))
            {
                SkipWhitespaceAndComments();
                continue;
            }
            else if (IsEndOfContent())
            {
                break;
            }
            else
            {
                _diagnostics.Add(new TinyDiagnostic(
                    TinyDiagnosticSeverity.Error, 
                    "Expected ',' or end of content", 
                    "", 0, 0));
                syntaxTree.IsValid = false;
                return syntaxTree;
            }
        }
        
        syntaxTree.IsValid = _diagnostics.Count == 0;
        syntaxTree.Diagnostics = _diagnostics;
        return syntaxTree;
    }
    
    private bool Match(string token)
    {
        if (_position + token.Length > _content.Length)
            return false;
        
        var substring = _content.Substring(_position, token.Length);
        if (substring == token)
        {
            _position += token.Length;
            return true;
        }
        
        return false;
    }
    
    private string ParseIdentifier()
    {
        var start = _position;
        
        if (_position >= _content.Length || !char.IsLetter(_content[_position]))
        {
            _diagnostics.Add(new TinyDiagnostic(
                TinyDiagnosticSeverity.Error, 
                "Expected identifier", 
                "", 0, 0));
            return "";
        }
        
        while (_position < _content.Length && 
               (char.IsLetterOrDigit(_content[_position]) || _content[_position] == '_'))
        {
            _position++;
        }
        
        return _content.Substring(start, _position - start);
    }
    
    private string ParseNamespace()
    {
        var start = _position;
        
        while (_position < _content.Length && 
               (_content[_position] != '\n' && _content[_position] != '\r'))
        {
            _position++;
        }
        
        var namespaceName = _content.Substring(start, _position - start).Trim();
        
        // Skip to end of line
        if (_position < _content.Length && (_content[_position] == '\n' || _content[_position] == '\r'))
        {
            _position++;
            if (_position < _content.Length && _content[_position] == '\r')
                _position++;
        }
        
        return namespaceName;
    }
    
    private string ParseUsing()
    {
        var start = _position;
        
        while (_position < _content.Length && 
               (_content[_position] != '\n' && _content[_position] != '\r'))
        {
            _position++;
        }
        
        var usingName = _content.Substring(start, _position - start).Trim();
        
        // Skip to end of line
        if (_position < _content.Length && (_content[_position] == '\n' || _content[_position] == '\r'))
        {
            _position++;
            if (_position < _content.Length && _content[_position] == '\r')
                _position++;
        }
        
        return usingName;
    }
    
    private TinyProperty ParseProperty()
    {
        if (IsEndOfContent())
            return null;
        
        var propertyName = ParseIdentifier();
        if (string.IsNullOrEmpty(propertyName))
            return null;
        
        string type = "string"; // default type
        int mode = 0; // default mode
        
        // Parse type if present
        if (Match(":"))
        {
            var typeIdentifier = ParseIdentifier();
            if (string.IsNullOrEmpty(typeIdentifier))
            {
                _diagnostics.Add(new TinyDiagnostic(
                    TinyDiagnosticSeverity.Error, 
                    "Expected type identifier after ':'", 
                    "", 0, 0));
                return null;
            }
            
            // Check for primitive alias
            type = GetPrimitiveType(typeIdentifier);
            if (string.IsNullOrEmpty(type))
            {
                // Not a primitive alias, use as-is
                type = typeIdentifier;
            }
        }
        
        // Parse mode if present
        if (Match("|"))
        {
            while (_position < _content.Length && char.IsWhiteSpace(_content[_position]))
            {
                _position++;
            }

            var modeStart = _position;
            while (_position < _content.Length && char.IsDigit(_content[_position]))
            {
                _position++;
            }

            var modeStr = _content.Substring(modeStart, _position - modeStart);
            if (!int.TryParse(modeStr, out mode))
            {
                _diagnostics.Add(new TinyDiagnostic(
                    TinyDiagnosticSeverity.Error, 
                    "Expected numeric mode after '|'", 
                    "", 0, 0));
                return null;
            }
            
            if (mode < 0 || mode > 2)
            {
                _diagnostics.Add(new TinyDiagnostic(
                    TinyDiagnosticSeverity.Error, 
                    "Mode must be 0, 1, or 2", 
                    "", 0, 0));
                return null;
            }
        }
        
        return new TinyProperty(propertyName, type, mode);
    }
    
    private string GetPrimitiveType(string alias)
    {
        return alias.ToLower() switch
        {
            "s" => "string",
            "i" => "int",
            "l" => "long",
            "b" => "bool",
            "d" => "double",
            "m" => "decimal",
            "f" => "float",
            "c" => "char",
            "by" => "byte",
            "dt" => "DateTime",
            "g" => "Guid",
            "o" => "object",
            _ => null
        };
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
            
            if (_content[_position] == '/' && _position + 1 < _content.Length && _content[_position + 1] == '/')
            {
                // Skip to end of line
                while (_position < _content.Length && _content[_position] != '\n' && _content[_position] != '\r')
                {
                    _position++;
                }
                
                if (_position < _content.Length && _content[_position] == '\n')
                {
                    _position++;
                }
                else if (_position < _content.Length && _content[_position] == '\r')
                {
                    _position++;
                    if (_position < _content.Length && _content[_position] == '\n')
                    {
                        _position++;
                    }
                }
                
                continue;
            }
            
            // Not whitespace or comment, break
            break;
        }
    }
    
    private bool IsEndOfContent()
    {
        return _position >= _content.Length;
    }
}

public sealed class TinySyntaxTree
{
    public string Namespace { get; set; } = "";
    public string ClassName { get; set; } = "";
    public List<TinyProperty> Properties { get; set; } = new List<TinyProperty>();
    public List<string> Usings { get; set; } = new List<string>();
    public bool IsValid { get; set; } = true;
    public List<TinyDiagnostic> Diagnostics { get; set; } = new List<TinyDiagnostic>();
}

public sealed class TinyProperty
{
    public TinyProperty(string name, string type, int mode)
    {
        Name = name;
        Type = type;
        Mode = mode;
    }
    
    public string Name { get; }
    public string Type { get; }
    public int Mode { get; }
}
