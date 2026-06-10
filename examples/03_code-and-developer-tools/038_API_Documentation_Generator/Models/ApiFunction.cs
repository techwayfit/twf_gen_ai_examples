namespace _038_API_Documentation_Generator.Models;

public sealed class ApiFunction
{
    public string Name { get; set; } = string.Empty;
    public string Declaration { get; set; } = string.Empty;
    public string Visibility { get; set; } = "public";
    public string ReturnType { get; set; } = "void";
    public List<FunctionParameter> Parameters { get; set; } = new();
    public int LineNumber { get; set; }
    public string? XmlDocSummary { get; set; }
    public string? XmlDocReturns { get; set; }
    public string? HttpMethod { get; set; }
    public string? RouteTemplate { get; set; }
    public bool IsControllerAction { get; set; }
}

public sealed class FunctionParameter
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool HasDefault { get; set; }
    public string? DefaultValue { get; set; }
    public string? Description { get; set; }
    public bool IsFromBody { get; set; }
    public bool IsFromQuery { get; set; }
    public bool IsFromRoute { get; set; }
}
