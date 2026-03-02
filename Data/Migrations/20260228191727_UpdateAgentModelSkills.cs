using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentControlPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAgentModelSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Skills_Agents_AgentId",
                table: "Skills");

            migrationBuilder.DropIndex(
                name: "IX_Skills_AgentId",
                table: "Skills");

            migrationBuilder.DropColumn(
                name: "AgentId",
                table: "Skills");

            migrationBuilder.RenameColumn(
                name: "AccessTokens",
                table: "Agents",
                newName: "SystemPrompt");

            migrationBuilder.CreateTable(
                name: "AgentSkill",
                columns: table => new
                {
                    AgentsId = table.Column<int>(type: "integer", nullable: false),
                    SkillsId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSkill", x => new { x.AgentsId, x.SkillsId });
                    table.ForeignKey(
                        name: "FK_AgentSkill_Agents_AgentsId",
                        column: x => x.AgentsId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentSkill_Skills_SkillsId",
                        column: x => x.SkillsId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentSkill_SkillsId",
                table: "AgentSkill",
                column: "SkillsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentSkill");

            migrationBuilder.RenameColumn(
                name: "SystemPrompt",
                table: "Agents",
                newName: "AccessTokens");

            migrationBuilder.AddColumn<int>(
                name: "AgentId",
                table: "Skills",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Skills_AgentId",
                table: "Skills",
                column: "AgentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Skills_Agents_AgentId",
                table: "Skills",
                column: "AgentId",
                principalTable: "Agents",
                principalColumn: "Id");
        }
    }
}
