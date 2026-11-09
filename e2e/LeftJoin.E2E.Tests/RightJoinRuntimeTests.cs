using static LeftJoin.E2E.Tests.SampleData;

namespace LeftJoin.E2E.Tests;

public class RightJoinRuntimeTests
{
    [Fact]
    public void FollowedBySelect_KeepsUnmatchedInnerElementsWithNullOuter()
    {
        var result =
            from d in Departments
            rightjoin s in Students on d.Id equals s.DepartmentId
            select (s.Name, Department: d?.Name);

        (string, string?)[] expected =
        [
            ("Ada", "Math"),
            ("Barbara", null),
            ("Grace", "Physics"),
            ("Grace", "Physics Annex"),
            ("Linus", null),
        ];
        Assert.Equal(expected, result.Order());
    }

    [Fact]
    public void MatchesMethodSyntaxRightJoin()
    {
        var querySyntax =
            from d in Departments
            rightjoin s in Students on d.Id equals s.DepartmentId
            select (s.Name, Department: d?.Name);

        var methodSyntax = Departments.RightJoin(
            Students,
            d => d.Id,
            s => s.DepartmentId,
            (d, s) => (s.Name, Department: d?.Name));

        Assert.Equal(methodSyntax, querySyntax);
    }

    [Fact]
    public void FollowedByOtherClauses_ChainsThroughTransparentIdentifiers()
    {
        var result =
            from d in Departments
            rightjoin s in Students on d.Id equals s.DepartmentId
            where s.IsActive
            orderby s.Name, d?.Name
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

    [Fact]
    public void AfterSeveralRangeVariables_UnmatchedInnerGetsNullForEveryOuterRangeVariable()
    {
        var result =
            from s in Students
            from d in Departments
            where s.DepartmentId == d.Id
            rightjoin a in Advisors on s.Id equals a.StudentId
            select (Student: s?.Name, Department: d?.Name, Advisor: a.Name);

        (string?, string?, string)[] expected =
        [
            ("Ada", "Math", "Turing"),
            (null, null, "Hopper"),
        ];
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AfterLet_UnmatchedInnerGetsDefaultForValueTypeRangeVariable()
    {
        var result =
            from s in Students
            where s.DepartmentId == 1
            let k = s.Id * 10
            rightjoin a in Advisors on s.Id equals a.StudentId
            select (k, a.Name);

        (int, string)[] expected =
        [
            (10, "Turing"),
            (0, "Hopper"),
        ];
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AfterSeveralRangeVariables_WorksInExpressionTrees()
    {
        var query =
            from s in Students.AsQueryable()
            from d in Departments
            where s.DepartmentId == d.Id
            rightjoin a in Advisors on s.Id equals a.StudentId
            select new { Student = s, Department = d, Advisor = a.Name };

        var result = query.AsEnumerable().Select(x => (x.Student?.Name, x.Department?.Name, x.Advisor));

        (string?, string?, string)[] expected =
        [
            ("Ada", "Math", "Turing"),
            (null, null, "Hopper"),
        ];
        Assert.Equal(expected, result);
    }
}
