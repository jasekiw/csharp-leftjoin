using Microsoft.EntityFrameworkCore;

namespace EfCoreMariaDb;

public sealed class Student
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Department
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

public sealed class Advisor
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public required string Name { get; set; }
}

public sealed class UniversityContext(DbContextOptions<UniversityContext> options) : DbContext(options)
{
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Advisor> Advisors => Set<Advisor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Explicit keys so seeding controls the ids the joins use.
        modelBuilder.Entity<Student>().Property(s => s.Id).ValueGeneratedNever();
        modelBuilder.Entity<Department>().Property(d => d.Id).ValueGeneratedNever();
        modelBuilder.Entity<Advisor>().Property(a => a.Id).ValueGeneratedNever();
    }

    public void Seed()
    {
        Departments.AddRange(
            new Department { Id = 1, Name = "Math" },
            new Department { Id = 2, Name = "Physics" },
            new Department { Id = 3, Name = "Chemistry" });

        // Linus and Barbara reference departments that do not exist.
        Students.AddRange(
            new Student { Id = 1, Name = "Ada", DepartmentId = 1 },
            new Student { Id = 2, Name = "Grace", DepartmentId = 2 },
            new Student { Id = 3, Name = "Linus", DepartmentId = 0 },
            new Student { Id = 4, Name = "Barbara", DepartmentId = 99, IsActive = false },
            new Student { Id = 5, Name = "Edsger", DepartmentId = 2 });

        // Knuth advises a student who does not exist.
        Advisors.AddRange(
            new Advisor { Id = 1, StudentId = 1, Name = "Turing" },
            new Advisor { Id = 2, StudentId = 3, Name = "Hopper" },
            new Advisor { Id = 3, StudentId = 42, Name = "Knuth" });

        SaveChanges();
    }
}
