using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentControlPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSkillModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Structure",
                table: "Skills",
                newName: "SkillMd");

            migrationBuilder.AddColumn<string>(
                name: "ScriptsJson",
                table: "Skills",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScriptsJson",
                table: "Skills");

            migrationBuilder.RenameColumn(
                name: "SkillMd",
                table: "Skills",
                newName: "Structure");
        }
    }
}
