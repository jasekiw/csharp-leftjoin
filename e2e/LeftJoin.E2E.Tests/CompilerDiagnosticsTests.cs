using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LeftJoin.E2E.Tests;

/// <summary>
/// Compiles snippets in memory with the locally built Microsoft.CodeAnalysis.CSharp package,
/// for behavior that can only be observed as diagnostics.
/// </summary>
public class CompilerDiagnosticsTests
{
    private const string Prelude = """
        using System.Linq;
        record Student(int Id, string Name, int DepartmentId);
        record Department(int Id, string Name);
        static class Queries
        {
            static Student[] students = [];
            static Department[] departments = [];
        """;

    [Fact]
    public void LeftJoin_ParsesAsJoinClauseWithLeftJoinKeyword()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "var q = from s in students leftjoin d in departments on s.DepartmentId equals d.Id select d;",
            new CSharpParseOptions(LanguageVersion.Preview));

        var join = tree.GetRoot().DescendantNodes().OfType<JoinClauseSyntax>().Single();
        Assert.Equal(SyntaxKind.LeftJoinKeyword, join.JoinKeyword.Kind());
        Assert.Equal("leftjoin", join.JoinKeyword.Text);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void RightJoin_ParsesAsJoinClauseWithRightJoinKeyword()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "var q = from d in departments rightjoin s in students on d.Id equals s.DepartmentId select s;",
            new CSharpParseOptions(LanguageVersion.Preview));

        var join = tree.GetRoot().DescendantNodes().OfType<JoinClauseSyntax>().Single();
        Assert.Equal(SyntaxKind.RightJoinKeyword, join.JoinKeyword.Kind());
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void LeftJoin_InnerRangeVariableIsNullable()
    {
        var diagnostics = Compile("""
                static object M() =>
                    from s in students
                    leftjoin d in departments on s.DepartmentId equals d.Id
                    select d.Name;
            """);

        AssertSingleError(diagnostics, "CS8602", "d");
    }

    [Fact]
    public void LeftJoin_InnerRangeVariableStaysNullableThroughLaterClauses()
    {
        var diagnostics = Compile("""
                static object M() =>
                    from s in students
                    leftjoin d in departments on s.DepartmentId equals d.Id
                    where s.Id > 0
                    select d.Name;
            """);

        AssertSingleError(diagnostics, "CS8602", "d");
    }

    [Fact]
    public void LeftJoin_OuterRangeVariableIsNotNullable()
    {
        var diagnostics = Compile("""
                static object M() =>
                    from s in students
                    leftjoin d in departments on s.DepartmentId equals d.Id
                    select s.Name + d?.Name;
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void RightJoin_OuterRangeVariableIsNullable()
    {
        var diagnostics = Compile("""
                static object M() =>
                    from d in departments
                    rightjoin s in students on d.Id equals s.DepartmentId
                    select d.Name;
            """);

        AssertSingleError(diagnostics, "CS8602", "d");
    }

    [Fact]
    public void RightJoin_EveryOuterRangeVariableIsNullable()
    {
        var diagnostics = Compile("""
                static object M() =>
                    from s in students
                    from d in departments
                    rightjoin t in students on s.Id equals t.Id
                    select d.Name + t.Name;
            """);

        AssertSingleError(diagnostics, "CS8602", "d");
    }

    [Fact]
    public void QueryWithoutOuterJoin_KeepsExistingNullability()
    {
        var diagnostics = Compile("""
                static string?[] names = [];
                static object M() =>
                    from x in names
                    join y in names on x equals y
                    where x.Length > 0
                    select x.Length + y.Length;
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void LeftJoin_RequiresPreviewLanguageVersion()
    {
        var diagnostics = Compile("""
                static object M() =>
                    from s in students
                    leftjoin d in departments on s.DepartmentId equals d.Id
                    select d?.Name;
            """,
            LanguageVersion.CSharp14);

        AssertSingleError(diagnostics, "CS8652", "leftjoin");
    }

    [Fact]
    public void LeftJoin_WithInto_IsAnError()
    {
        var diagnostics = Compile("""
                static object M() =>
                    from s in students
                    leftjoin d in departments on s.DepartmentId equals d.Id into g
                    select g;
            """);

        var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("into", error.Location.SourceTree!.ToString().Substring(error.Location.SourceSpan.Start, error.Location.SourceSpan.Length));
    }

    [Fact]
    public void LeftJoin_SourceWithoutLeftJoinMethod_ReportsMissingQueryPattern()
    {
        var diagnostics = Compile("""
                class NoLeftJoin
                {
                    public NoLeftJoin Select(System.Func<int, int> f) => this;
                }
                static object M() =>
                    from x in new NoLeftJoin()
                    leftjoin y in new[] { 1 } on x equals y
                    select x;
            """);

        var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("CS1936", error.Id);
        Assert.Contains("LeftJoin", error.GetMessage());
    }

    private static void AssertSingleError(IEnumerable<Diagnostic> diagnostics, string id, string spanText)
    {
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(spanText, diagnostic.Location.SourceTree!.ToString().Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
    }

    private static IReadOnlyList<Diagnostic> Compile(string members, LanguageVersion languageVersion = LanguageVersion.Preview)
    {
        var tree = CSharpSyntaxTree.ParseText(Prelude + members + "\n}\n", new CSharpParseOptions(languageVersion));
        var compilation = CSharpCompilation.Create(
            "Snippet",
            [tree],
            RuntimeReferences.Value,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));

        return compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToList();
    }

    private static readonly Lazy<MetadataReference[]> RuntimeReferences = new(() =>
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetDirectoryName(path) == runtimeDirectory)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    });
}
