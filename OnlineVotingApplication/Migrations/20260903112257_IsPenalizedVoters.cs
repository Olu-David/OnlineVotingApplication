using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineVotingApplication.Migrations
{
    /// <inheritdoc />
    public partial class IsPenalizedVoters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPenalized",
                table: "Votes",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPenalized",
                table: "Votes");
        }
    }
}
