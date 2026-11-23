using DrivingSchool.Core;
using Microsoft.EntityFrameworkCore;

namespace DrivingSchool.Data;

public sealed class SchoolDb(DbContextOptions<SchoolDb> options) : DbContext(options)
{
    public DbSet<SchoolUser> Users => Set<SchoolUser>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<PracticalLesson> Lessons => Set<PracticalLesson>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SchoolUser>().HasIndex(user => user.Login).IsUnique();
        model.Entity<Student>().HasIndex(student => student.Name);
        model.HasDbFunction(typeof(SchoolStore).GetMethod(nameof(SchoolStore.SchoolContains))!)
            .HasName("school_contains").IsBuiltIn();
        model.Entity<SchoolUser>().HasData(
            new SchoolUser { Id = 1, Login = "admin", DisplayName = "Администратор", Role = Roles.Admin, PasswordHash = "156c866cce67a1ce184b5be59d8733f1:b3cc2c2c9ac261cc60c95c9cd7183f4ad7add54ef4c40342d646fc0548423d2b" },
            new SchoolUser { Id = 2, Login = "manager", DisplayName = "Менеджер", Role = Roles.Manager, PasswordHash = "db21132a48f18ce8cc2a649a9c1c0c2e:32d9de8605484ec3fa170be5c21274fdcf8677b7b1667594551c5a9bd9a0b602" },
            new SchoolUser { Id = 3, Login = "nikita", DisplayName = "Никита", Role = Roles.Instructor, PasswordHash = "78599bed37d6ab9c1761cf819edeb958:b847150b722befff273aad3efdfbdaf60fc63531fd71666039513c8992c9bcf3" },
            new SchoolUser { Id = 4, Login = "sergey", DisplayName = "Сергей", Role = Roles.Instructor, PasswordHash = "21f16ba13f663c0f392c497391aa9b0d:5456bdce51c98d8d6af1245b95b1abef9946f8f24d1eed4b064c5441e1f57367" });
        var lesson = model.Entity<PracticalLesson>();
        lesson.HasIndex(row => new { row.Status, row.CreatedAt });
        lesson.HasIndex(row => new { row.InstructorId, row.CreatedAt });
        lesson.Property(row => row.Version).IsConcurrencyToken();
        var utc = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        lesson.Property(row => row.CreatedAt).HasConversion(utc);
        lesson.Property(row => row.CompletedAt).HasConversion(utc);
        model.Entity<Student>().Property(row => row.RegisteredAt).HasConversion(utc);
        lesson.HasOne(row => row.Student).WithMany().HasForeignKey(row => row.StudentId).OnDelete(DeleteBehavior.Restrict);
        lesson.HasOne(row => row.Instructor).WithMany().HasForeignKey(row => row.InstructorId).OnDelete(DeleteBehavior.Restrict);
    }
}
