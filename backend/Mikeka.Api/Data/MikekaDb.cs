using Microsoft.EntityFrameworkCore;
using Mikeka.Api.Domain;

namespace Mikeka.Api.Data;

public class MikekaDb(DbContextOptions<MikekaDb> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Slip> Slips => Set<Slip>();
    public DbSet<SlipPick> SlipPicks => Set<SlipPick>();
    public DbSet<RunLog> RunLogs => Set<RunLog>();
    public DbSet<BettingSettings> Settings => Set<BettingSettings>();
    public DbSet<BalanceEntry> BalanceHistory => Set<BalanceEntry>();

    /// <summary>Every change of an account's stored balance also goes into the balance history.</summary>
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var e in ChangeTracker.Entries<Account>().Where(e => e.State == EntityState.Modified).ToList())
        {
            var p = e.Property(a => a.LastBalance);
            if (p.IsModified && p.CurrentValue is { } balance && p.CurrentValue != p.OriginalValue)
                BalanceHistory.Add(new BalanceEntry { AccountId = e.Entity.Id, Balance = balance });
        }
        return await base.SaveChangesAsync(ct);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<BalanceEntry>(e =>
        {
            e.HasIndex(x => new { x.AccountId, x.At });
            e.Property(x => x.Balance).HasPrecision(18, 2);
        });

        b.Entity<Account>(e =>
        {
            e.HasIndex(a => new { a.Url, a.Username }).IsUnique();
            e.Property(a => a.LastBalance).HasPrecision(18, 2);
            e.Property(a => a.BaseStake).HasPrecision(18, 2);
            e.OwnsMany(a => a.Markets, m => m.ToJson());
        });

        b.Entity<Slip>(e =>
        {
            e.HasIndex(s => new { s.AccountId, s.BetDay });
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(s => s.Stake).HasPrecision(18, 2);
            e.Property(s => s.CombinedOdds).HasPrecision(10, 3);
            e.Property(s => s.PotentialReturn).HasPrecision(18, 2);
            e.Property(s => s.BalanceBefore).HasPrecision(18, 2);
            e.Property(s => s.BalanceAfter).HasPrecision(18, 2);
            e.HasMany(s => s.Picks).WithOne().HasForeignKey(p => p.SlipId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SlipPick>(e =>
        {
            e.Property(p => p.Line).HasPrecision(6, 1);
            e.Property(p => p.Odds).HasPrecision(8, 3);
        });

        b.Entity<RunLog>().HasIndex(l => l.At);

        b.Entity<BettingSettings>(e =>
        {
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.MinPickOdds).HasPrecision(8, 3);
            e.Property(s => s.MaxPickOdds).HasPrecision(8, 3);
            e.Property(s => s.MinCombinedOdds).HasPrecision(8, 3);
            e.Property(s => s.MaxCombinedOdds).HasPrecision(8, 3);
            e.HasData(new BettingSettings
            {
                Id = 1, MinPickOdds = 1.10m, MaxPickOdds = 1.20m, MinCombinedOdds = 2.10m, MaxCombinedOdds = 2.20m,
                UpdatedAt = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
            });
        });
    }
}
