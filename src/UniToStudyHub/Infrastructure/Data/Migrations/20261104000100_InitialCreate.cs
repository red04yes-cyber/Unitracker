using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UniToStudyHub.Infrastructure.Data;

#nullable disable

namespace UniToStudyHub.Infrastructure.Data.Migrations;

[DbContext(typeof(StudyHubDbContext))]
[Migration("20261104000100_InitialCreate")]
public sealed class InitialCreate : Migration
{
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Announcements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", nullable: false),
                    CourseName = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsNew = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announcements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CareerExams",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Cfu = table.Column<int>(type: "INTEGER", nullable: false),
                    CountsTowardAverage = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerExams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Courses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MoodleCourseId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Cfu = table.Column<int>(type: "INTEGER", nullable: false),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Grades",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", nullable: false),
                    CourseName = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<int>(type: "INTEGER", nullable: false),
                    Honors = table.Column<bool>(type: "INTEGER", nullable: false),
                    Cfu = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CountsTowardAverage = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LegacyImports",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyImports", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "PlannedExams",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CourseId = table.Column<string>(type: "TEXT", nullable: false),
                    CourseName = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Cfu = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedStudyHours = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlannedSession = table.Column<string>(type: "TEXT", nullable: false),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedExams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfileSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GraduationTarget = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MoodleBaseUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExamAttempts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ExamId = table.Column<string>(type: "TEXT", nullable: false),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeDecision = table.Column<int>(type: "INTEGER", nullable: false),
                    NumericGrade = table.Column<int>(type: "INTEGER", nullable: true),
                    IsHonors = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPassFail = table.Column<bool>(type: "INTEGER", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamAttempts_CareerExams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "CareerExams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_SourceKey",
                table: "Announcements",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_MoodleCourseId",
                table: "Courses",
                column: "MoodleCourseId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_ExamId_AttemptedAt",
                table: "ExamAttempts",
                columns: new[] { "ExamId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Grades_CourseId",
                table: "Grades",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedExams_CourseId",
                table: "PlannedExams",
                column: "CourseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Announcements");

            migrationBuilder.DropTable(
                name: "Courses");

            migrationBuilder.DropTable(
                name: "ExamAttempts");

            migrationBuilder.DropTable(
                name: "Grades");

            migrationBuilder.DropTable(
                name: "LegacyImports");

            migrationBuilder.DropTable(
                name: "PlannedExams");

            migrationBuilder.DropTable(
                name: "ProfileSettings");

            migrationBuilder.DropTable(
                name: "CareerExams");
        }

        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.0");

            modelBuilder.Entity("UniToStudyHub.Infrastructure.Data.Entities.AnnouncementEntity", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("TEXT");

                    b.Property<string>("CourseName")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<bool>("IsNew")
                        .HasColumnType("INTEGER");

                    b.Property<string>("SourceKey")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Summary")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("UpdatedAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("Url")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("SourceKey");

                    b.ToTable("Announcements", (string)null);
                });

            modelBuilder.Entity("UniToStudyHub.Infrastructure.Data.Entities.CareerExamEntity", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("TEXT");

                    b.Property<int>("Cfu")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("CountsTowardAverage")
                        .HasColumnType("INTEGER");

                    b.Property<string>("CourseId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("CareerExams", (string)null);
                });

            modelBuilder.Entity("UniToStudyHub.Infrastructure.Data.Entities.CourseEntity", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("TEXT");

                    b.Property<int>("Cfu")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Code")
                        .IsRequired()
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT")
                        .HasDefaultValue("");

                    b.Property<bool>("Completed")
                        .HasColumnType("INTEGER");

                    b.Property<int?>("MoodleCourseId")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("MoodleCourseId");

                    b.ToTable("Courses", (string)null);
                });

            modelBuilder.Entity("UniToStudyHub.Infrastructure.Data.Entities.ExamAttemptEntity", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("TEXT");

                    b.Property<DateTimeOffset>("AttemptedAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("ExamId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("GradeDecision")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsHonors")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("IsPassFail")
                        .HasColumnType("INTEGER");

                    b.Property<int?>("NumericGrade")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Outcome")
                        .HasColumnType("INTEGER");

                    b.Property<DateTimeOffset?>("RecordedAt")
                        .HasColumnType("TEXT");

                    b.Property<int>("Status")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("ExamId", "AttemptedAt");

                    b.ToTable("ExamAttempts", (string)null);
                });

            modelBuilder.Entity("UniToStudyHub.Infrastructure.Data.Entities.GradeEntity", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("TEXT");

                    b.Property<int>("Cfu")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("CountsTowardAverage")
                        .HasColumnType("INTEGER");

                    b.Property<string>("CourseId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("CourseName")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("Date")
                        .HasColumnType("TEXT");

                    b.Property<bool>("Honors")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Value")
                        .HasColumnType("INTEGER");

                    b.HasKey("Id");

                    b.HasIndex("CourseId");

                    b.ToTable("Grades", (string)null);
                });

            modelBuilder.Entity("UniToStudyHub.Infrastructure.Data.Entities.LegacyImportEntity", b =>
                {
                    b.Property<string>("Key")
                        .HasColumnType("TEXT");

                    b.Property<DateTimeOffset>("ImportedAt")
                        .HasColumnType("TEXT");

                    b.Property<string>("SourceHash")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.ToTable("LegacyImports", (string)null);
                });

            modelBuilder.Entity("UniToStudyHub.Infrastructure.Data.Entities.PlannedExamEntity", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("TEXT");

                    b.Property<int>("Cfu")
                        .HasColumnType("INTEGER");

                    b.Property<bool>("Completed")
                        .HasColumnType("INTEGER");

                    b.Property<string>("CourseId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("CourseName")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("EstimatedStudyHours")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime?>("PlannedDate")
                        .HasColumnType("TEXT");

                    b.Property<string>("PlannedSession")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("CourseId");

                    b.ToTable("PlannedExams", (string)null);
                });

            modelBuilder.Entity("UniToStudyHub.Infrastructure.Data.Entities.ProfileSettingsEntity", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<DateTime>("GraduationTarget")
                        .HasColumnType("TEXT");

                    b.Property<string>("MoodleBaseUrl")
                        .IsRequired()
                        .HasMaxLength(2048)
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("ProfileSettings", (string)null);
                });

            modelBuilder.Entity("UniToStudyHub.Infrastructure.Data.Entities.ExamAttemptEntity", b =>
                {
                    b.HasOne("UniToStudyHub.Infrastructure.Data.Entities.CareerExamEntity", null)
                        .WithMany("Attempts")
                        .HasForeignKey("ExamId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("UniToStudyHub.Infrastructure.Data.Entities.CareerExamEntity", b =>
                {
                    b.Navigation("Attempts");
                });
#pragma warning restore 612, 618
        }
}
