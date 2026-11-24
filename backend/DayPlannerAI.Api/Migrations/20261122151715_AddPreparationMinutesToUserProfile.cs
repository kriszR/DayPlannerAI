using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DayPlannerAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPreparationMinutesToUserProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreparationMinutes",
                table: "UserProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "UserProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "PreparationMinutes",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreparationMinutes",
                table: "UserProfiles");
        }
    }
}
