using Microsoft.EntityFrameworkCore;
using XerifeBot.Api.Models;
using XerifeBot.Api.Services;

namespace XerifeBot.Api.Data;

public class AppDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();
    public DbSet<User> Users => Set<User>();
    public DbSet<CustomerSession> CustomerSessions => Set<CustomerSession>();
    public DbSet<BoardGame> BoardGames => Set<BoardGame>();
    public DbSet<MessageLog> MessageLogs => Set<MessageLog>();
    public DbSet<BotSetting> BotSettings => Set<BotSetting>();
    public DbSet<InstagramCommentLog> InstagramCommentLogs => Set<InstagramCommentLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configurações e índices de Tenants
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasIndex(e => e.WhatsAppInstanceName);
            entity.HasIndex(e => e.InstagramAccountId);
        });

        // Configurações e índices de Users
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.TenantId);
        });

        // Índices no RemoteJid da sessão do cliente e Canal por Tenant
        modelBuilder.Entity<CustomerSession>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.RemoteJid }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.Channel });
        });

        // Configurações do BoardGame
        modelBuilder.Entity<BoardGame>(entity =>
        {
            entity.Property(e => e.RentalPrice)
                  .HasPrecision(10, 2);
            entity.Property(e => e.SalePrice)
                  .HasPrecision(10, 2);
            entity.Property(e => e.AquisitionPrice)
                  .HasPrecision(10, 2);

            entity.HasIndex(e => new { e.TenantId, e.Name });
            entity.HasIndex(e => new { e.TenantId, e.IsAvailable });
        });

        // Configuração de índices para performance do MessageLog
        modelBuilder.Entity<MessageLog>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.RemoteJid });
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
        });

        // Configurações e índices de InstagramCommentLog
        modelBuilder.Entity<InstagramCommentLog>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.CommentId });
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt });
        });

        // =====================================================================
        // Filtros Globais Multi-Tenant (EF Core Global Query Filters)
        // Se houver um TenantId ativo no contexto, todas as consultas filtram automaticamente
        // =====================================================================
        if (_tenantContext != null)
        {
            modelBuilder.Entity<CustomerSession>()
                .HasQueryFilter(e => !_tenantContext.CurrentTenantId.HasValue || e.TenantId == _tenantContext.CurrentTenantId.Value);

            modelBuilder.Entity<BoardGame>()
                .HasQueryFilter(e => !_tenantContext.CurrentTenantId.HasValue || e.TenantId == _tenantContext.CurrentTenantId.Value);

            modelBuilder.Entity<MessageLog>()
                .HasQueryFilter(e => !_tenantContext.CurrentTenantId.HasValue || e.TenantId == _tenantContext.CurrentTenantId.Value);

            modelBuilder.Entity<InstagramCommentLog>()
                .HasQueryFilter(e => !_tenantContext.CurrentTenantId.HasValue || e.TenantId == _tenantContext.CurrentTenantId.Value);

            modelBuilder.Entity<TenantSettings>()
                .HasQueryFilter(e => !_tenantContext.CurrentTenantId.HasValue || e.TenantId == _tenantContext.CurrentTenantId.Value);
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext?.CurrentTenantId.HasValue == true)
        {
            var currentTenantId = _tenantContext.CurrentTenantId.Value;
            foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
            {
                if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                {
                    entry.Entity.TenantId = currentTenantId;
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
