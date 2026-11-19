using System.ComponentModel.DataAnnotations;
using Badger.Class;
using Microsoft.EntityFrameworkCore;

namespace Badger.Classes
{
    public class BadgerContext : DbContext
    {
        public DbSet<GamePost> GamePosts { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            var db = "Badger";
            options.UseSqlServer(Util.GetDBConnectionString(db));
            options.EnableSensitiveDataLogging(true);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<GamePost>().ToTable("GamePosts");
    }

    public class GamePost
    {
        [Key] public int Id { get; set; }
        public string GameValue { get; set; } = "";
        public string CustomName { get; set; }
        public string CustomDuration { get; set; }
        public string Comments { get; set; } = "";
        public long MessageId { get; set; }
        public long ChannelId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ScheduledFor { get; set; }
        public DateTime DeleteAt { get; set; }
        public bool Pinged { get; set; }
        public bool Announced { get; set; }
        public string PlayerIds { get; set; } = "";
    }

    public static class Data
    {
        public static void InitializeDatabase()
        {
            using BadgerContext db = new();
            db.Database.ExecuteSqlRaw(@"
IF OBJECT_ID(N'dbo.GamePosts', N'U') IS NULL
CREATE TABLE dbo.GamePosts (
    Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_GamePosts PRIMARY KEY,
    GameValue NVARCHAR(50) NOT NULL,
    CustomName NVARCHAR(200) NULL,
    CustomDuration NVARCHAR(200) NULL,
    Comments NVARCHAR(MAX) NOT NULL,
    MessageId BIGINT NOT NULL,
    ChannelId BIGINT NOT NULL,
    CreatedAt DATETIME2 NOT NULL,
    ScheduledFor DATETIME2 NULL,
    DeleteAt DATETIME2 NOT NULL,
    Pinged BIT NOT NULL,
    Announced BIT NOT NULL,
    PlayerIds NVARCHAR(MAX) NOT NULL
);");
        }
    }
}
