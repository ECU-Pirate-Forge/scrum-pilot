using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ScrumPilot.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistedPlanningFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite defers AddUniqueConstraint's table rebuild until the migration's end.
            // The backfill needs a valid composite parent key before that rebuild.
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
                migrationBuilder.Sql("""
                    CREATE UNIQUE INDEX "IX_Planning_Stories_ProjectPbi" ON "Stories" ("ProjectId", "PbiId");
                    """);

            migrationBuilder.DropIndex(
                name: "IX_Stories_ProjectId",
                table: "Stories");

            migrationBuilder.AddColumn<int>(
                name: "GenerationRunId",
                table: "Stories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentStoryId",
                table: "Stories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProblemStatementId",
                table: "Stories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Stories",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "CurrentGuidelinesRevisionId",
                table: "Project",
                type: "integer",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Stories_ProjectId_PbiId",
                table: "Stories",
                columns: new[] { "ProjectId", "PbiId" });

            migrationBuilder.CreateTable(
                name: "DependencyReviews",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    PbiId = table.Column<int>(type: "integer", nullable: false),
                    InvalidationRevision = table.Column<long>(type: "bigint", nullable: false),
                    ReviewedRevision = table.Column<long>(type: "bigint", nullable: true),
                    ReviewedByActorId = table.Column<string>(type: "text", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DependencyReviews", x => new { x.ProjectId, x.PbiId });
                    table.CheckConstraint("CK_DependencyReview_Revision", "\"InvalidationRevision\" >= 0 AND (\"ReviewedRevision\" IS NULL OR (\"ReviewedRevision\" >= 0 AND \"ReviewedRevision\" <= \"InvalidationRevision\"))");
                    table.ForeignKey(
                        name: "FK_DependencyReviews_Stories_ProjectId_PbiId",
                        columns: x => new { x.ProjectId, x.PbiId },
                        principalTable: "Stories",
                        principalColumns: new[] { "ProjectId", "PbiId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GenerationRuns",
                columns: table => new
                {
                    GenerationRunId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "text", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerationRuns", x => x.GenerationRunId);
                    table.UniqueConstraint("AK_GenerationRuns_ProjectId_GenerationRunId", x => new { x.ProjectId, x.GenerationRunId });
                    table.ForeignKey(
                        name: "FK_GenerationRuns_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PbiDependencies",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    DependentPbiId = table.Column<int>(type: "integer", nullable: false),
                    PrerequisitePbiId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PbiDependencies", x => new { x.ProjectId, x.DependentPbiId, x.PrerequisitePbiId });
                    table.CheckConstraint("CK_Dependency_NotSelf", "\"DependentPbiId\" <> \"PrerequisitePbiId\"");
                    table.ForeignKey(
                        name: "FK_PbiDependencies_Stories_ProjectId_DependentPbiId",
                        columns: x => new { x.ProjectId, x.DependentPbiId },
                        principalTable: "Stories",
                        principalColumns: new[] { "ProjectId", "PbiId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PbiDependencies_Stories_ProjectId_PrerequisitePbiId",
                        columns: x => new { x.ProjectId, x.PrerequisitePbiId },
                        principalTable: "Stories",
                        principalColumns: new[] { "ProjectId", "PbiId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProblemStatements",
                columns: table => new
                {
                    ProblemStatementId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemStatements", x => x.ProblemStatementId);
                    table.UniqueConstraint("AK_ProblemStatements_ProjectId_ProblemStatementId", x => new { x.ProjectId, x.ProblemStatementId });
                    table.ForeignKey(
                        name: "FK_ProblemStatements_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectGraphStates",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectGraphStates", x => x.ProjectId);
                    table.ForeignKey(
                        name: "FK_ProjectGraphStates_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectGuidelinesRevisions",
                columns: table => new
                {
                    ProjectGuidelinesRevisionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectGuidelinesRevisions", x => x.ProjectGuidelinesRevisionId);
                    table.UniqueConstraint("AK_ProjectGuidelinesRevisions_ProjectId_ProjectGuidelinesRevis~", x => new { x.ProjectId, x.ProjectGuidelinesRevisionId });
                    table.CheckConstraint("CK_Guidelines_Revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_ProjectGuidelinesRevisions_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProjectRepositories",
                columns: table => new
                {
                    ProjectRepositoryId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    GitHubRepositoryId = table.Column<long>(type: "bigint", nullable: false),
                    InstallationId = table.Column<long>(type: "bigint", nullable: false),
                    Owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DefaultBranch = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectRepositories", x => x.ProjectRepositoryId);
                    table.UniqueConstraint("AK_ProjectRepositories_ProjectId_ProjectRepositoryId", x => new { x.ProjectId, x.ProjectRepositoryId });
                    table.ForeignKey(
                        name: "FK_ProjectRepositories_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RepositorySnapshots",
                columns: table => new
                {
                    RepositorySnapshotId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    ProjectRepositoryId = table.Column<int>(type: "integer", nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TreeSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TreeJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositorySnapshots", x => x.RepositorySnapshotId);
                    table.UniqueConstraint("AK_RepositorySnapshots_ProjectId_RepositorySnapshotId", x => new { x.ProjectId, x.RepositorySnapshotId });
                    table.ForeignKey(
                        name: "FK_RepositorySnapshots_ProjectRepositories_ProjectId_ProjectRe~",
                        columns: x => new { x.ProjectId, x.ProjectRepositoryId },
                        principalTable: "ProjectRepositories",
                        principalColumns: new[] { "ProjectId", "ProjectRepositoryId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepositorySnapshots_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskPullRequestBindings",
                columns: table => new
                {
                    TaskPullRequestBindingId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    PbiId = table.Column<int>(type: "integer", nullable: false),
                    ProjectRepositoryId = table.Column<int>(type: "integer", nullable: false),
                    PullRequestNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskPullRequestBindings", x => x.TaskPullRequestBindingId);
                    table.UniqueConstraint("AK_TaskPullRequestBindings_ProjectId_TaskPullRequestBindingId", x => new { x.ProjectId, x.TaskPullRequestBindingId });
                    table.CheckConstraint("CK_Binding_Number", "\"PullRequestNumber\" > 0");
                    table.ForeignKey(
                        name: "FK_TaskPullRequestBindings_ProjectRepositories_ProjectId_Proje~",
                        columns: x => new { x.ProjectId, x.ProjectRepositoryId },
                        principalTable: "ProjectRepositories",
                        principalColumns: new[] { "ProjectId", "ProjectRepositoryId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskPullRequestBindings_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskPullRequestBindings_Stories_ProjectId_PbiId",
                        columns: x => new { x.ProjectId, x.PbiId },
                        principalTable: "Stories",
                        principalColumns: new[] { "ProjectId", "PbiId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GenerationInputs",
                columns: table => new
                {
                    GenerationInputId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    GenerationRunId = table.Column<int>(type: "integer", nullable: false),
                    ProjectGuidelinesRevisionId = table.Column<int>(type: "integer", nullable: true),
                    ProblemStatementId = table.Column<int>(type: "integer", nullable: true),
                    RepositorySnapshotId = table.Column<int>(type: "integer", nullable: true),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GenerationInputs", x => x.GenerationInputId);
                    table.UniqueConstraint("AK_GenerationInputs_ProjectId_GenerationInputId", x => new { x.ProjectId, x.GenerationInputId });
                    table.ForeignKey(
                        name: "FK_GenerationInputs_GenerationRuns_ProjectId_GenerationRunId",
                        columns: x => new { x.ProjectId, x.GenerationRunId },
                        principalTable: "GenerationRuns",
                        principalColumns: new[] { "ProjectId", "GenerationRunId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GenerationInputs_ProblemStatements_ProjectId_ProblemStateme~",
                        columns: x => new { x.ProjectId, x.ProblemStatementId },
                        principalTable: "ProblemStatements",
                        principalColumns: new[] { "ProjectId", "ProblemStatementId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GenerationInputs_ProjectGuidelinesRevisions_ProjectId_Proje~",
                        columns: x => new { x.ProjectId, x.ProjectGuidelinesRevisionId },
                        principalTable: "ProjectGuidelinesRevisions",
                        principalColumns: new[] { "ProjectId", "ProjectGuidelinesRevisionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GenerationInputs_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GenerationInputs_RepositorySnapshots_ProjectId_RepositorySn~",
                        columns: x => new { x.ProjectId, x.RepositorySnapshotId },
                        principalTable: "RepositorySnapshots",
                        principalColumns: new[] { "ProjectId", "RepositorySnapshotId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskSpecificationRevisions",
                columns: table => new
                {
                    TaskSpecificationRevisionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    PbiId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ScopeMode = table.Column<string>(type: "text", nullable: false),
                    Approval = table.Column<string>(type: "text", nullable: false),
                    ApprovedByActorId = table.Column<string>(type: "text", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GenerationRunId = table.Column<int>(type: "integer", nullable: true),
                    ProjectGuidelinesRevisionId = table.Column<int>(type: "integer", nullable: true),
                    RepositorySnapshotId = table.Column<int>(type: "integer", nullable: true),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSpecificationRevisions", x => x.TaskSpecificationRevisionId);
                    table.UniqueConstraint("AK_TaskSpecificationRevisions_ProjectId_TaskSpecificationRevis~", x => new { x.ProjectId, x.TaskSpecificationRevisionId });
                    table.CheckConstraint("CK_Specification_Revision", "\"Revision\" > 0");
                    table.ForeignKey(
                        name: "FK_TaskSpecificationRevisions_GenerationRuns_ProjectId_Generat~",
                        columns: x => new { x.ProjectId, x.GenerationRunId },
                        principalTable: "GenerationRuns",
                        principalColumns: new[] { "ProjectId", "GenerationRunId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskSpecificationRevisions_ProjectGuidelinesRevisions_Proje~",
                        columns: x => new { x.ProjectId, x.ProjectGuidelinesRevisionId },
                        principalTable: "ProjectGuidelinesRevisions",
                        principalColumns: new[] { "ProjectId", "ProjectGuidelinesRevisionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskSpecificationRevisions_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskSpecificationRevisions_RepositorySnapshots_ProjectId_Re~",
                        columns: x => new { x.ProjectId, x.RepositorySnapshotId },
                        principalTable: "RepositorySnapshots",
                        principalColumns: new[] { "ProjectId", "RepositorySnapshotId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskSpecificationRevisions_Stories_ProjectId_PbiId",
                        columns: x => new { x.ProjectId, x.PbiId },
                        principalTable: "Stories",
                        principalColumns: new[] { "ProjectId", "PbiId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PullRequestScopeChecks",
                columns: table => new
                {
                    PullRequestScopeCheckId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    TaskPullRequestBindingId = table.Column<int>(type: "integer", nullable: false),
                    TaskSpecificationRevisionId = table.Column<int>(type: "integer", nullable: false),
                    HeadSha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequestScopeChecks", x => x.PullRequestScopeCheckId);
                    table.UniqueConstraint("AK_PullRequestScopeChecks_ProjectId_PullRequestScopeCheckId", x => new { x.ProjectId, x.PullRequestScopeCheckId });
                    table.ForeignKey(
                        name: "FK_PullRequestScopeChecks_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequestScopeChecks_TaskPullRequestBindings_ProjectId_Ta~",
                        columns: x => new { x.ProjectId, x.TaskPullRequestBindingId },
                        principalTable: "TaskPullRequestBindings",
                        principalColumns: new[] { "ProjectId", "TaskPullRequestBindingId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PullRequestScopeChecks_TaskSpecificationRevisions_ProjectId~",
                        columns: x => new { x.ProjectId, x.TaskSpecificationRevisionId },
                        principalTable: "TaskSpecificationRevisions",
                        principalColumns: new[] { "ProjectId", "TaskSpecificationRevisionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskAllowedFiles",
                columns: table => new
                {
                    TaskAllowedFileId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    TaskSpecificationRevisionId = table.Column<int>(type: "integer", nullable: false),
                    Path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: false),
                    RenameFromPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Rationale = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAllowedFiles", x => x.TaskAllowedFileId);
                    table.UniqueConstraint("AK_TaskAllowedFiles_ProjectId_TaskAllowedFileId", x => new { x.ProjectId, x.TaskAllowedFileId });
                    table.CheckConstraint("CK_AllowedFile_Rename", "(\"Operation\" = 'Rename' AND \"RenameFromPath\" IS NOT NULL) OR (\"Operation\" <> 'Rename' AND \"RenameFromPath\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_TaskAllowedFiles_Project_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Project",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskAllowedFiles_TaskSpecificationRevisions_ProjectId_TaskS~",
                        columns: x => new { x.ProjectId, x.TaskSpecificationRevisionId },
                        principalTable: "TaskSpecificationRevisions",
                        principalColumns: new[] { "ProjectId", "TaskSpecificationRevisionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stories_ProjectId_GenerationRunId",
                table: "Stories",
                columns: new[] { "ProjectId", "GenerationRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_Stories_ProjectId_ParentStoryId",
                table: "Stories",
                columns: new[] { "ProjectId", "ParentStoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Stories_ProjectId_ProblemStatementId",
                table: "Stories",
                columns: new[] { "ProjectId", "ProblemStatementId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Stories_Parent",
                table: "Stories",
                sql: "\"ParentStoryId\" IS NULL OR \"ParentStoryId\" <> \"PbiId\"");

            migrationBuilder.CreateIndex(
                name: "IX_Project_ProjectId_CurrentGuidelinesRevisionId",
                table: "Project",
                columns: new[] { "ProjectId", "CurrentGuidelinesRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationInputs_GenerationRunId",
                table: "GenerationInputs",
                column: "GenerationRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GenerationInputs_ProjectId_GenerationRunId",
                table: "GenerationInputs",
                columns: new[] { "ProjectId", "GenerationRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationInputs_ProjectId_ProblemStatementId",
                table: "GenerationInputs",
                columns: new[] { "ProjectId", "ProblemStatementId" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationInputs_ProjectId_ProjectGuidelinesRevisionId",
                table: "GenerationInputs",
                columns: new[] { "ProjectId", "ProjectGuidelinesRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationInputs_ProjectId_RepositorySnapshotId",
                table: "GenerationInputs",
                columns: new[] { "ProjectId", "RepositorySnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_GenerationRuns_ProjectId_Kind_IdempotencyKey",
                table: "GenerationRuns",
                columns: new[] { "ProjectId", "Kind", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PbiDependencies_ProjectId_PrerequisitePbiId",
                table: "PbiDependencies",
                columns: new[] { "ProjectId", "PrerequisitePbiId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProblemStatements_ProjectId_IsArchived",
                table: "ProblemStatements",
                columns: new[] { "ProjectId", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectGuidelinesRevisions_ProjectId_Revision",
                table: "ProjectGuidelinesRevisions",
                columns: new[] { "ProjectId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRepositories_ProjectId_GitHubRepositoryId",
                table: "ProjectRepositories",
                columns: new[] { "ProjectId", "GitHubRepositoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestScopeChecks_ProjectId_TaskPullRequestBindingId",
                table: "PullRequestScopeChecks",
                columns: new[] { "ProjectId", "TaskPullRequestBindingId" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestScopeChecks_ProjectId_TaskSpecificationRevisionId",
                table: "PullRequestScopeChecks",
                columns: new[] { "ProjectId", "TaskSpecificationRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PullRequestScopeChecks_TaskPullRequestBindingId_HeadSha_Tas~",
                table: "PullRequestScopeChecks",
                columns: new[] { "TaskPullRequestBindingId", "HeadSha", "TaskSpecificationRevisionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositorySnapshots_ProjectId_ProjectRepositoryId",
                table: "RepositorySnapshots",
                columns: new[] { "ProjectId", "ProjectRepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_RepositorySnapshots_ProjectRepositoryId_CommitSha",
                table: "RepositorySnapshots",
                columns: new[] { "ProjectRepositoryId", "CommitSha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskAllowedFiles_ProjectId_TaskSpecificationRevisionId",
                table: "TaskAllowedFiles",
                columns: new[] { "ProjectId", "TaskSpecificationRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskAllowedFiles_TaskSpecificationRevisionId_Path",
                table: "TaskAllowedFiles",
                columns: new[] { "TaskSpecificationRevisionId", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskPullRequestBindings_PbiId_ProjectRepositoryId_PullReque~",
                table: "TaskPullRequestBindings",
                columns: new[] { "PbiId", "ProjectRepositoryId", "PullRequestNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskPullRequestBindings_ProjectId_PbiId",
                table: "TaskPullRequestBindings",
                columns: new[] { "ProjectId", "PbiId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskPullRequestBindings_ProjectId_ProjectRepositoryId",
                table: "TaskPullRequestBindings",
                columns: new[] { "ProjectId", "ProjectRepositoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskSpecificationRevisions_PbiId_Revision",
                table: "TaskSpecificationRevisions",
                columns: new[] { "PbiId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskSpecificationRevisions_ProjectId_GenerationRunId",
                table: "TaskSpecificationRevisions",
                columns: new[] { "ProjectId", "GenerationRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskSpecificationRevisions_ProjectId_PbiId",
                table: "TaskSpecificationRevisions",
                columns: new[] { "ProjectId", "PbiId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskSpecificationRevisions_ProjectId_ProjectGuidelinesRevis~",
                table: "TaskSpecificationRevisions",
                columns: new[] { "ProjectId", "ProjectGuidelinesRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskSpecificationRevisions_ProjectId_RepositorySnapshotId",
                table: "TaskSpecificationRevisions",
                columns: new[] { "ProjectId", "RepositorySnapshotId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Project_ProjectGuidelinesRevisions_ProjectId_CurrentGuideli~",
                table: "Project",
                columns: new[] { "ProjectId", "CurrentGuidelinesRevisionId" },
                principalTable: "ProjectGuidelinesRevisions",
                principalColumns: new[] { "ProjectId", "ProjectGuidelinesRevisionId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stories_GenerationRuns_ProjectId_GenerationRunId",
                table: "Stories",
                columns: new[] { "ProjectId", "GenerationRunId" },
                principalTable: "GenerationRuns",
                principalColumns: new[] { "ProjectId", "GenerationRunId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stories_ProblemStatements_ProjectId_ProblemStatementId",
                table: "Stories",
                columns: new[] { "ProjectId", "ProblemStatementId" },
                principalTable: "ProblemStatements",
                principalColumns: new[] { "ProjectId", "ProblemStatementId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stories_Stories_ProjectId_ParentStoryId",
                table: "Stories",
                columns: new[] { "ProjectId", "ParentStoryId" },
                principalTable: "Stories",
                principalColumns: new[] { "ProjectId", "PbiId" },
                onDelete: ReferentialAction.Restrict);

            // Keep the legacy field verbatim, including missing/cross-project/self
            // references, so diagnostics can still explain their original intent.
            migrationBuilder.Sql("""
                INSERT INTO "PbiDependencies" ("ProjectId", "DependentPbiId", "PrerequisitePbiId")
                SELECT d."ProjectId", d."PbiId", p."PbiId"
                FROM "Stories" d
                INNER JOIN "Stories" p ON p."PbiId" = d."DependsOnPbiId" AND p."ProjectId" = d."ProjectId"
                WHERE d."PbiId" <> p."PbiId";
                INSERT INTO "ProjectGraphStates" ("ProjectId", "Revision")
                SELECT "ProjectId", 0 FROM "Project";
                """);

            AddAppendOnlyGuards(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
                migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);
            RemoveAppendOnlyGuards(migrationBuilder);
            migrationBuilder.DropForeignKey(
                name: "FK_Project_ProjectGuidelinesRevisions_ProjectId_CurrentGuideli~",
                table: "Project");

            migrationBuilder.DropForeignKey(
                name: "FK_Stories_GenerationRuns_ProjectId_GenerationRunId",
                table: "Stories");

            migrationBuilder.DropForeignKey(
                name: "FK_Stories_ProblemStatements_ProjectId_ProblemStatementId",
                table: "Stories");

            migrationBuilder.DropForeignKey(
                name: "FK_Stories_Stories_ProjectId_ParentStoryId",
                table: "Stories");

            migrationBuilder.DropTable(
                name: "DependencyReviews");

            migrationBuilder.DropTable(
                name: "GenerationInputs");

            migrationBuilder.DropTable(
                name: "PbiDependencies");

            migrationBuilder.DropTable(
                name: "ProjectGraphStates");

            migrationBuilder.DropTable(
                name: "PullRequestScopeChecks");

            migrationBuilder.DropTable(
                name: "TaskAllowedFiles");

            migrationBuilder.DropTable(
                name: "ProblemStatements");

            migrationBuilder.DropTable(
                name: "TaskPullRequestBindings");

            migrationBuilder.DropTable(
                name: "TaskSpecificationRevisions");

            migrationBuilder.DropTable(
                name: "GenerationRuns");

            migrationBuilder.DropTable(
                name: "ProjectGuidelinesRevisions");

            migrationBuilder.DropTable(
                name: "RepositorySnapshots");

            migrationBuilder.DropTable(
                name: "ProjectRepositories");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Stories_ProjectId_PbiId",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Stories_ProjectId_GenerationRunId",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Stories_ProjectId_ParentStoryId",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Stories_ProjectId_ProblemStatementId",
                table: "Stories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Stories_Parent",
                table: "Stories");

            migrationBuilder.DropIndex(
                name: "IX_Project_ProjectId_CurrentGuidelinesRevisionId",
                table: "Project");

            migrationBuilder.DropColumn(
                name: "GenerationRunId",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "ParentStoryId",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "ProblemStatementId",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Stories");

            migrationBuilder.DropColumn(
                name: "CurrentGuidelinesRevisionId",
                table: "Project");

            migrationBuilder.CreateIndex(
                name: "IX_Stories_ProjectId",
                table: "Stories",
                column: "ProjectId");

            if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
                migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
        }

        private static readonly string[] AppendOnlyTables =
        [
            "ProjectGuidelinesRevisions", "TaskSpecificationRevisions", "TaskAllowedFiles",
            "GenerationInputs", "RepositorySnapshots", "TaskPullRequestBindings", "PullRequestScopeChecks"
        ];

        private void AddAppendOnlyGuards(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
                    CREATE FUNCTION planning_reject_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN
                        RAISE EXCEPTION 'Planning history is append-only';
                    END;
                    $$;
                    """);
                foreach (var table in AppendOnlyTables)
                    migrationBuilder.Sql($"""
                        CREATE TRIGGER "TR_{table}_AppendOnly"
                        BEFORE UPDATE OR DELETE ON "{table}"
                        FOR EACH ROW EXECUTE FUNCTION planning_reject_mutation();
                        """);
            }
            else
            {
                foreach (var table in AppendOnlyTables)
                {
                    foreach (var operation in new[] { "UPDATE", "DELETE" })
                        migrationBuilder.Sql($"""
                            CREATE TRIGGER "TR_{table}_{operation}"
                            BEFORE {operation} ON "{table}"
                            BEGIN SELECT RAISE(ABORT, 'Planning history is append-only'); END;
                            """);
                }
            }
        }

        private void RemoveAppendOnlyGuards(MigrationBuilder migrationBuilder)
        {
            foreach (var table in AppendOnlyTables)
            {
                if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
                    migrationBuilder.Sql($"""DROP TRIGGER "TR_{table}_AppendOnly" ON "{table}";""");
                else
                    foreach (var operation in new[] { "UPDATE", "DELETE" })
                        migrationBuilder.Sql($"""DROP TRIGGER "TR_{table}_{operation}";""");
            }
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
                migrationBuilder.Sql("DROP FUNCTION planning_reject_mutation();");
        }
    }
}
