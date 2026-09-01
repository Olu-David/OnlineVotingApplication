using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineVotingApplication.Migrations
{
    /// <inheritdoc />
    public partial class Dbroleseed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Votes_ElectionEvents_ElectionEventId",
                table: "Votes");

            migrationBuilder.DropIndex(
                name: "IX_Votes_ElectionEventId",
                table: "Votes");

            migrationBuilder.DropColumn(
                name: "ElectionEventId",
                table: "Votes");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_ElectionId",
                table: "Votes",
                column: "ElectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Votes_ElectionEvents_ElectionId",
                table: "Votes",
                column: "ElectionId",
                principalTable: "ElectionEvents",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Votes_ElectionEvents_ElectionId",
                table: "Votes");

            migrationBuilder.DropIndex(
                name: "IX_Votes_ElectionId",
                table: "Votes");

            migrationBuilder.AddColumn<Guid>(
                name: "ElectionEventId",
                table: "Votes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Votes_ElectionEventId",
                table: "Votes",
                column: "ElectionEventId");

            migrationBuilder.AddForeignKey(
                name: "FK_Votes_ElectionEvents_ElectionEventId",
                table: "Votes",
                column: "ElectionEventId",
                principalTable: "ElectionEvents",
                principalColumn: "Id");
        }
    }
}
