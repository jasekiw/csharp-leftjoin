using static LeftJoin.E2E.Tests.SampleData;

namespace LeftJoin.E2E.Tests;

public class LeftJoinRuntimeTests
{
    [Fact]
    public void FollowedBySelect_KeepsUnmatchedOuterElementsWithNullInner()
    {
        var result =
            from s in Students
            leftjoin d in Departments on s.DepartmentId equals d.Id
            select (s.Name, Department: d?.Name);

        (string, string?)[] expected =
        [
            ("Ada", "Math"),
            ("Grace", "Physics"),
            ("Grace", "Physics Annex"),
            ("Linus", null),
            ("Barbara", null),
        ];
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MatchesMethodSyntaxLeftJoin()
    {
        var querySyntax =
            from s in Students
            leftjoin d in Departments on s.DepartmentId equals d.Id
            select (s.Name, Department: d?.Name);

        var methodSyntax = Students.LeftJoin(
            Departments,
            s => s.DepartmentId,
            d => d.Id,
            (s, d) => (s.Name, Department: d?.Name));

        Assert.Equal(methodSyntax, querySyntax);
    }

    [Fact]
    public void MatchesClassicGroupJoinDefaultIfEmptyPattern()
    {
        var leftJoin =
            from s in Students
            leftjoin d in Departments on s.DepartmentId equals d.Id
            select (s.Name, Department: d?.Name);

        var classic =
            from s in Students
            join d in Departments on s.DepartmentId equals d.Id into departments
            from d in departments.DefaultIfEmpty()
            select (s.Name, Department: d?.Name);

        Assert.Equal(classic, leftJoin);
    }

    [Fact]
    public void FollowedByOtherClauses_ChainsThroughTransparentIdentifiers()
    {
        var result =
            from s in Students
            leftjoin d in Departments on s.DepartmentId equals d.Id
            where s.IsActive
            leftjoin a in Advisors on s.Id equals a.StudentId
            orderby s.Name, d?.Name
            select (s.Name, Department: d?.Name, Advisor: a?.Name);

        (string, string?, string?)[] expected =
        [
            ("Ada", "Math", "Turing"),
            ("Grace", "Physics", null),
            ("Grace", "Physics Annex", null),
            ("Linus", null, "Hopper"),
        ];
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExplicitRangeVariableType_CastsInnerSequence()
    {
        object[] boxedDepartments = Departments;

        var result =
            from s in Students
            leftjoin Department d in boxedDepartments on s.DepartmentId equals d.Id
            select (s.Name, Department: d?.Name);

        Assert.Equal(
            Students.LeftJoin(Departments, s => s.DepartmentId, d => d.Id, (s, d) => (s.Name, Department: d?.Name)),
            result);
    }

    [Fact]
    public void AfterPatternWithPropertySubpattern_IsLeftJoinNotPatternVariable()
    {
        var result =
            from s in Students
            where s is { IsActive: true }
            leftjoin d in Departments on s.DepartmentId equals d.Id
            select (s.Name, Department: d?.Name);

        (string, string?)[] expected =
        [
            ("Ada", "Math"),
            ("Grace", "Physics"),
            ("Grace", "Physics Annex"),
            ("Linus", null),
        ];
        Assert.Equal(expected, result);
    }
}
