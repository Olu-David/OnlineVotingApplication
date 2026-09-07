using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OnlineVotingApplication.Migrations
{
    /// <inheritdoc />
    public partial class DBroleseeder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingEmails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Recipient = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    IsSent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingEmails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    FolderName = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalVotes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_results", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "States",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_States", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupportTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    IpAdidress = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationName = table.Column<string>(type: "text", nullable: false),
                    TenantCategory = table.Column<int>(type: "integer", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    SubscriptionPlan = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MaxAllowedElections = table.Column<int>(type: "integer", nullable: false),
                    ProfilePicture = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "voterRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserID = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    hasVoted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_voterRegistrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Lgas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StateId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lgas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lgas_States_StateId",
                        column: x => x.StateId,
                        principalTable: "States",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "text", nullable: true),
                    DateOfBirth = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoterRegistrationID = table.Column<string>(type: "text", nullable: true),
                    VoteCastTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OfficialStaffId = table.Column<string>(type: "text", nullable: false),
                    DepartmentorAgency = table.Column<string>(type: "text", nullable: true),
                    StateId = table.Column<Guid>(type: "uuid", nullable: true),
                    HasVoted = table.Column<bool>(type: "boolean", nullable: false),
                    isVoter = table.Column<bool>(type: "boolean", nullable: false),
                    profileImage = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_States_StateId",
                        column: x => x.StateId,
                        principalTable: "States",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ElectionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ElectionYear = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ElectionEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Party",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Party", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Party_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ElectionCustomFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ElectionEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    FieldName = table.Column<string>(type: "text", nullable: false),
                    FieldType = table.Column<string>(type: "text", nullable: false),
                    FormCategory = table.Column<int>(type: "integer", nullable: false),
                    CustomChoicesCsv = table.Column<string>(type: "text", nullable: true),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionCustomFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ElectionCustomFields_ElectionEvents_ElectionEventId",
                        column: x => x.ElectionEventId,
                        principalTable: "ElectionEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElectionCustomFields_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Position",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ElectionEventId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Position", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Position_ElectionEvents_ElectionEventId",
                        column: x => x.ElectionEventId,
                        principalTable: "ElectionEvents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Position_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Candidate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    CandidateID = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Manifesto = table.Column<string>(type: "text", nullable: true),
                    CandidateImg = table.Column<string>(type: "text", nullable: true),
                    isDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    isApproved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ElectionEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    StateId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    PartyId = table.Column<Guid>(type: "uuid", nullable: true),
                    PositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LgaId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    StatesId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candidate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Candidate_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Candidate_ElectionEvents_ElectionEventId",
                        column: x => x.ElectionEventId,
                        principalTable: "ElectionEvents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Candidate_Lgas_LgaId",
                        column: x => x.LgaId,
                        principalTable: "Lgas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Candidate_Party_PartyId",
                        column: x => x.PartyId,
                        principalTable: "Party",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Candidate_Position_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Position",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Candidate_States_StateId",
                        column: x => x.StateId,
                        principalTable: "States",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Candidate_States_StatesId",
                        column: x => x.StatesId,
                        principalTable: "States",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Candidate_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "candidateInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ElectionEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    PositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CandidateEmail = table.Column<string>(type: "text", nullable: false),
                    CandidateName = table.Column<string>(type: "text", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_candidateInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_candidateInvitations_ElectionEvents_ElectionEventId",
                        column: x => x.ElectionEventId,
                        principalTable: "ElectionEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_candidateInvitations_Position_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Position",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_candidateInvitations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CandidateCustomValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateCustomValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateCustomValues_Candidate_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CandidateCustomValues_ElectionCustomFields_FieldId",
                        column: x => x.FieldId,
                        principalTable: "ElectionCustomFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CandidateCustomValues_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CandidateGalleries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateGalleries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateGalleries_Candidate_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidate",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CandidateGalleries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Votes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VoterId = table.Column<string>(type: "text", nullable: true),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: true),
                    StateId = table.Column<Guid>(type: "uuid", nullable: true),
                    ElectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CastAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VotedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    HasVoted = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmationCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsPenalized = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Votes_AspNetUsers_VoterId",
                        column: x => x.VoterId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Votes_Candidate_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "Candidate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Votes_ElectionEvents_ElectionId",
                        column: x => x.ElectionId,
                        principalTable: "ElectionEvents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Votes_Position_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Position",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Votes_States_StateId",
                        column: x => x.StateId,
                        principalTable: "States",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Votes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_StateId",
                table: "AspNetUsers",
                column: "StateId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Candidate_ElectionEventId",
                table: "Candidate",
                column: "ElectionEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidate_LgaId",
                table: "Candidate",
                column: "LgaId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidate_PartyId",
                table: "Candidate",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidate_PositionId",
                table: "Candidate",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidate_Slug",
                table: "Candidate",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Candidate_StateId",
                table: "Candidate",
                column: "StateId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidate_StatesId",
                table: "Candidate",
                column: "StatesId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidate_TenantId",
                table: "Candidate",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Candidate_UserId",
                table: "Candidate",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateCustomValues_CandidateId",
                table: "CandidateCustomValues",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateCustomValues_FieldId",
                table: "CandidateCustomValues",
                column: "FieldId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateCustomValues_TenantId",
                table: "CandidateCustomValues",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateGalleries_CandidateId",
                table: "CandidateGalleries",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateGalleries_TenantId",
                table: "CandidateGalleries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_candidateInvitations_ElectionEventId",
                table: "candidateInvitations",
                column: "ElectionEventId");

            migrationBuilder.CreateIndex(
                name: "IX_candidateInvitations_PositionId",
                table: "candidateInvitations",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_candidateInvitations_TenantId",
                table: "candidateInvitations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCustomFields_ElectionEventId",
                table: "ElectionCustomFields",
                column: "ElectionEventId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCustomFields_TenantId",
                table: "ElectionCustomFields",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionEvents_TenantId",
                table: "ElectionEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionEvents_Title",
                table: "ElectionEvents",
                column: "Title",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lgas_StateId",
                table: "Lgas",
                column: "StateId");

            migrationBuilder.CreateIndex(
                name: "IX_Party_TenantId",
                table: "Party",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Position_ElectionEventId",
                table: "Position",
                column: "ElectionEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Position_TenantId",
                table: "Position",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_CandidateId",
                table: "Votes",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_ElectionId",
                table: "Votes",
                column: "ElectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_PositionId",
                table: "Votes",
                column: "PositionId");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_StateId",
                table: "Votes",
                column: "StateId");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_TenantId",
                table: "Votes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_VoterId",
                table: "Votes",
                column: "VoterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "CandidateCustomValues");

            migrationBuilder.DropTable(
                name: "CandidateGalleries");

            migrationBuilder.DropTable(
                name: "candidateInvitations");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "PendingEmails");

            migrationBuilder.DropTable(
                name: "PendingFiles");

            migrationBuilder.DropTable(
                name: "results");

            migrationBuilder.DropTable(
                name: "SupportTickets");

            migrationBuilder.DropTable(
                name: "voterRegistrations");

            migrationBuilder.DropTable(
                name: "Votes");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "ElectionCustomFields");

            migrationBuilder.DropTable(
                name: "Candidate");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Lgas");

            migrationBuilder.DropTable(
                name: "Party");

            migrationBuilder.DropTable(
                name: "Position");

            migrationBuilder.DropTable(
                name: "States");

            migrationBuilder.DropTable(
                name: "ElectionEvents");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
