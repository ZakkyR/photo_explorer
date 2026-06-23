using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoExplorer.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "Folders",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "Folders");
        }
    }
}
