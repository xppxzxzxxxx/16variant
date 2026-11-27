using DrivingSchool.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Security.Cryptography;

namespace DrivingSchool.Data;

public static class SchoolDatabase
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DrivingSchool", "school.db");

    public static void Configure(DbContextOptionsBuilder options, string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            DefaultTimeout = 10
        }.ToString();
        options.UseSqlite(connectionString).AddInterceptors(new SearchFunctions());
    }

    public static async Task Initialize(SchoolDb db, string path, bool seedDemo = false, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await db.Database.MigrateAsync(ct);
        if (!seedDemo)
            return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await SchoolDemo.Seed(db, ct);
        await transaction.CommitAsync(ct);
    }

    public static string Hash(string password, string salt) => Convert.ToHexString(
        Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromHexString(salt), 210_000, HashAlgorithmName.SHA256, 32));

    public static bool Verify(string password, string passwordHash)
    {
        var separator = passwordHash.IndexOf(':');
        if (separator != 32 || passwordHash.Length != 97)
            return false;
        try
        {
            var salt = Convert.FromHexString(passwordHash.AsSpan(0, separator));
            var expected = Convert.FromHexString(passwordHash.AsSpan(separator + 1));
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed class SearchFunctions : Microsoft.EntityFrameworkCore.Diagnostics.DbConnectionInterceptor
    {
        public override void ConnectionOpened(System.Data.Common.DbConnection connection,
            Microsoft.EntityFrameworkCore.Diagnostics.ConnectionEndEventData eventData) => Register(connection);

        public override Task ConnectionOpenedAsync(System.Data.Common.DbConnection connection,
            Microsoft.EntityFrameworkCore.Diagnostics.ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Register(connection);
            return Task.CompletedTask;
        }

        private static void Register(System.Data.Common.DbConnection connection) =>
            ((SqliteConnection)connection).CreateFunction<string, string, bool>("school_contains",
                (value, term) => value.Contains(term, StringComparison.OrdinalIgnoreCase), isDeterministic: true);
    }
}

public sealed class SchoolDbFactory : IDesignTimeDbContextFactory<SchoolDb>
{
    public SchoolDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SchoolDb>();
        SchoolDatabase.Configure(options, SchoolDatabase.DefaultPath);
        return new SchoolDb(options.Options);
    }
}
