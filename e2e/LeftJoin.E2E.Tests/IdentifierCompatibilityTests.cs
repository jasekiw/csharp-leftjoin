using static LeftJoin.E2E.Tests.SampleData;

namespace LeftJoin.E2E.Tests;

/// <summary>
/// Code that compiles with the stock compiler today must keep compiling with the same meaning.
/// </summary>
public class IdentifierCompatibilityTests
{
    [Fact]
    public void LeftJoinAndRightJoin_RemainUsableAsNamesInsideQueries()
    {
        var leftjoin = 10;

        var result =
            from rightjoin in new[] { 1, 2 }
            let total = rightjoin + leftjoin
            select total;

        Assert.Equal([11, 12], result);
    }

    [Fact]
    public void LeftJoin_AsRangeVariableAndContinuationName()
    {
        var result =
            from leftjoin in Students
            select leftjoin.Name into rightjoin
            where rightjoin.StartsWith('G')
            select rightjoin;

        Assert.Equal(["Grace"], result);
    }

    [Fact]
    public void PatternVariableNamedLeftJoin_FollowedBySelect_KeepsMeaning()
    {
        object[] items = [new Department(1, "Math"), "not a department", new Department(2, "Physics")];

        var result =
            from x in items
            where x is Department leftjoin && leftjoin.Id > 1
            select x;

        Assert.Equal([items[2]], result);
    }

    [Fact]
    public void PatternVariableNamedRightJoin_FollowedByInnerJoin_KeepsMeaning()
    {
        object[] items = [Students[0], "not a student", Students[1]];

        var result =
            from x in items
            where x is Student rightjoin
            join d in Departments on ((Student)x).DepartmentId equals d.Id
            select d.Name;

        Assert.Equal(["Math", "Physics", "Physics Annex"], result);
    }
}
