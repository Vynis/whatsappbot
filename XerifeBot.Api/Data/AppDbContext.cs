using Microsoft.EntityFrameworkCore;
using XerifeBot.Api.Models;

namespace XerifeBot.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<CustomerSession> CustomerSessions => Set<CustomerSession>();
    public DbSet<BoardGame> BoardGames => Set<BoardGame>();
    public DbSet<MessageLog> MessageLogs => Set<MessageLog>();
    public DbSet<BotSetting> BotSettings => Set<BotSetting>();
    public DbSet<InstagramCommentLog> InstagramCommentLogs => Set<InstagramCommentLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Índice no RemoteJid da sessão do cliente e Canal
        modelBuilder.Entity<CustomerSession>(entity =>
        {
            entity.HasIndex(e => e.RemoteJid).IsUnique();
            entity.HasIndex(e => e.Channel);
        });

        // Configurações do BoardGame
        modelBuilder.Entity<BoardGame>(entity =>
        {
            entity.Property(e => e.RentalPrice)
                  .HasPrecision(10, 2);
        });

        // Configuração de índices para performance do MessageLog
        modelBuilder.Entity<MessageLog>(entity =>
        {
            entity.HasIndex(e => e.RemoteJid);
            entity.HasIndex(e => e.CreatedAt);
        });

        // Configurações e índices de InstagramCommentLog
        modelBuilder.Entity<InstagramCommentLog>(entity =>
        {
            entity.HasIndex(e => e.CommentId);
            entity.HasIndex(e => e.CreatedAt);
        });
    }
}
