using EfCoreMariaDb;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("MARIADB_CONNECTION")
    ?? "Server=127.0.0.1;Port=3307;Database=university;User=demo;Password=demo;";

var options = new DbContextOptionsBuilder<UniversityContext>().UseMySQL(connectionString).Options;
using var db = new UniversityContext(options);

db.Database.EnsureDeleted();
db.Database.EnsureCreated();
db.Seed();

var failures = 0;

Check("leftjoin: every student, with their department or null",
    from s in db.Students
    leftjoin d in db.Departments on s.DepartmentId equals d.Id
    select new { Student = s.Name, Department = d == null ? null : d.Name },
    x => $"{x.Student} | {x.Department ?? "-"}",
    ["Ada | Math", "Grace | Physics", "Linus | -", "Barbara | -", "Edsger | Physics"]);

Check("rightjoin: every department, with its students or null",
    from s in db.Students
    rightjoin d in db.Departments on s.DepartmentId equals d.Id
    select new { Department = d.Name, Student = s == null ? null : s.Name },
    x => $"{x.Department} | {x.Student ?? "-"}",
    ["Math | Ada", "Physics | Grace", "Physics | Edsger", "Chemistry | -"]);

Check("leftjoin twice, with where and orderby between them",
    from s in db.Students
    leftjoin d in db.Departments on s.DepartmentId equals d.Id
    where s.IsActive
    leftjoin a in db.Advisors on s.Id equals a.StudentId
    orderby s.Name
    select new { Student = s.Name, Department = d == null ? null : d.Name, Advisor = a == null ? null : a.Name },
    x => $"{x.Student} | {x.Department ?? "-"} | {x.Advisor ?? "-"}",
    ["Ada | Math | Turing", "Edsger | Physics | -", "Grace | Physics | -", "Linus | - | Hopper"],
    ordered: true);

// Every range variable declared before the rightjoin is null when an advisor has no matching student.
Check("rightjoin after two range variables",
    from s in db.Students
    join d in db.Departments on s.DepartmentId equals d.Id
    rightjoin a in db.Advisors on s.Id equals a.StudentId
    select new { Advisor = a.Name, Student = s, Department = d },
    x => $"{x.Advisor} | {x.Student?.Name ?? "-"} | {x.Department?.Name ?? "-"}",
    ["Turing | Ada | Math", "Hopper | - | -", "Knuth | - | -"]);

Check("rightjoin after where",
    from s in db.Students
    where s.IsActive
    rightjoin d in db.Departments on s.DepartmentId equals d.Id
    select new { Department = d.Name, Student = s == null ? null : s.Name },
    x => $"{x.Department} | {x.Student ?? "-"}",
    ["Math | Ada", "Physics | Grace", "Physics | Edsger", "Chemistry | -"],
    knownIssue: "EF Core 10 applies the outer Where after the RIGHT JOIN, dropping unmatched rows; " +
        "the same happens with the RightJoin method and the stock compiler");

Check("same rows as the LeftJoin method",
    db.Students.LeftJoin(
        db.Departments,
        s => s.DepartmentId,
        d => d.Id,
        (s, d) => new { Student = s.Name, Department = d == null ? null : d.Name }),
    x => $"{x.Student} | {x.Department ?? "-"}",
    ["Ada | Math", "Grace | Physics", "Linus | -", "Barbara | -", "Edsger | Physics"]);

Console.WriteLine(failures == 0 ? "All scenarios passed." : $"{failures} scenario(s) failed.");
return failures == 0 ? 0 : 1;

void Check<T>(string title, IQueryable<T> query, Func<T, string> format, string[] expected, bool ordered = false, string? knownIssue = null)
{
    Console.WriteLine($"== {title}");
    try
    {
        Console.WriteLine(Indent(query.ToQueryString()));
        var rows = query.AsEnumerable().Select(format).ToList();
        Console.WriteLine("   rows:");
        rows.ForEach(row => Console.WriteLine($"     {row}"));

        var matches = ordered ? rows.SequenceEqual(expected) : rows.Order().SequenceEqual(expected.Order());
        if (matches)
        {
            Console.WriteLine(knownIssue is null ? "   OK" : "   OK (the known issue below appears to be fixed)");
        }
        else if (knownIssue is not null)
        {
            Console.WriteLine($"   KNOWN ISSUE, expected: {string.Join(", ", expected)}");
        }
        else
        {
            failures++;
            Console.WriteLine($"   MISMATCH, expected: {string.Join(", ", expected)}");
        }

        if (knownIssue is not null)
        {
            Console.WriteLine($"   ({knownIssue})");
        }
    }
    catch (Exception e)
    {
        failures++;
        Console.WriteLine($"   FAILED: {e.GetType().Name}: {e.Message}");
    }

    Console.WriteLine();
}

static string Indent(string text) =>
    string.Join(Environment.NewLine, text.Split('\n').Select(line => "   " + line.TrimEnd('\r')));
