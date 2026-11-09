namespace LeftJoin.E2E.Tests;

public sealed record Student(int Id, string Name, int DepartmentId, bool IsActive = true);

public sealed record Department(int Id, string Name);

public sealed record Advisor(int StudentId, string Name);

public static class SampleData
{
    public static readonly Student[] Students =
    [
        new(1, "Ada", DepartmentId: 1),
        new(2, "Grace", DepartmentId: 2),
        new(3, "Linus", DepartmentId: 0),
        new(4, "Barbara", DepartmentId: 99, IsActive: false),
    ];

    public static readonly Department[] Departments =
    [
        new(1, "Math"),
        new(2, "Physics"),
        new(2, "Physics Annex"),
        new(3, "Chemistry"),
    ];

    public static readonly Advisor[] Advisors =
    [
        new(StudentId: 1, "Turing"),
        new(StudentId: 3, "Hopper"),
    ];
}
