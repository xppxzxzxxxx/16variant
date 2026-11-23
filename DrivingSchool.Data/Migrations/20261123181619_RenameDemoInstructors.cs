using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrivingSchool.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameDemoInstructors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DisplayName", "Login", "PasswordHash" },
                values: new object[] { "Никита", "nikita", "78599bed37d6ab9c1761cf819edeb958:b847150b722befff273aad3efdfbdaf60fc63531fd71666039513c8992c9bcf3" });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "DisplayName", "Login", "PasswordHash" },
                values: new object[] { "Сергей", "sergey", "21f16ba13f663c0f392c497391aa9b0d:5456bdce51c98d8d6af1245b95b1abef9946f8f24d1eed4b064c5441e1f57367" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DisplayName", "Login", "PasswordHash" },
                values: new object[] { "Иван", "ivan", "8a8e567de20058ad3005d19b1ce8c01b:55220fd0dc9584683b1f47a940f185731dbfffbf6cbdb1cd21aeb592eac1b636" });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "DisplayName", "Login", "PasswordHash" },
                values: new object[] { "Павел", "pavel", "1d466cc8296ff44e9c06761e61f3268c:1d2ca8917b667386065cb6c046a1f2118390ef37ca1897223e348dbf3beebcc6" });
        }
    }
}
