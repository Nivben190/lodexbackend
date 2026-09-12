using Looxdex.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Looxdex.Api.Data;

public class LooxdexDbContext : DbContext
{
    public LooxdexDbContext(DbContextOptions<LooxdexDbContext> options) : base(options)
    {
    }

    public DbSet<FeedPostEntity> FeedPosts => Set<FeedPostEntity>();
    public DbSet<DetectedItemEntity> DetectedItems => Set<DetectedItemEntity>();
    public DbSet<ShoppingAlternativeEntity> ShoppingAlternatives => Set<ShoppingAlternativeEntity>();
    public DbSet<SavedPostEntity> SavedPosts => Set<SavedPostEntity>();
    public DbSet<ClosetItemEntity> ClosetItems => Set<ClosetItemEntity>();
    public DbSet<SuitcaseEntity> Suitcases => Set<SuitcaseEntity>();
    public DbSet<EventGroupEntity> EventGroups => Set<EventGroupEntity>();
    public DbSet<PackingItemEntity> PackingItems => Set<PackingItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Embeddings are stored as raw little-endian float32 bytes rather than JSON:
        // a 512-dim CLIP vector is 2KB this way versus ~6KB as text. When this moves
        // to Postgres in phase 1 the column becomes a pgvector and this converter goes away.
        var embeddingConverter = new ValueConverter<float[]?, byte[]?>(
            v => v == null ? null : FloatArrayToBytes(v),
            v => v == null ? null : BytesToFloatArray(v));

        var embeddingComparer = new ValueComparer<float[]?>(
            (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
            v => v == null ? 0 : v.Aggregate(0, (acc, f) => HashCode.Combine(acc, f.GetHashCode())),
            v => v == null ? null : v.ToArray());

        modelBuilder.Entity<FeedPostEntity>(entity =>
        {
            entity.HasIndex(e => new { e.Source, e.ExternalId }).IsUnique();
            entity.HasIndex(e => e.Rank).IsDescending();
            entity.HasIndex(e => e.DetectionState);

            entity.HasMany(e => e.DetectedItems)
                .WithOne(d => d.FeedPost)
                .HasForeignKey(d => d.FeedPostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DetectedItemEntity>(entity =>
        {
            entity.Property(e => e.Embedding)
                .HasConversion(embeddingConverter)
                .Metadata.SetValueComparer(embeddingComparer);

            entity.HasMany(e => e.Alternatives)
                .WithOne(a => a.DetectedItem)
                .HasForeignKey(a => a.DetectedItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShoppingAlternativeEntity>()
            .Property(e => e.Price)
            .HasPrecision(10, 2);

        modelBuilder.Entity<SavedPostEntity>(entity =>
        {
            // One row per owner per post; makes toggling idempotent.
            entity.HasIndex(e => new { e.OwnerKey, e.FeedPostId }).IsUnique();

            entity.HasOne(e => e.FeedPost)
                .WithMany()
                .HasForeignKey(e => e.FeedPostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClosetItemEntity>(entity =>
        {
            entity.HasIndex(e => e.OwnerKey);

            entity.Property(e => e.Embedding)
                .HasConversion(embeddingConverter)
                .Metadata.SetValueComparer(embeddingComparer);
        });

        modelBuilder.Entity<SuitcaseEntity>(entity =>
        {
            entity.HasIndex(e => e.OwnerKey);

            entity.HasMany(e => e.EventGroups)
                .WithOne(g => g.Suitcase)
                .HasForeignKey(g => g.SuitcaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EventGroupEntity>()
            .HasMany(e => e.Items)
            .WithOne(i => i.EventGroup)
            .HasForeignKey(i => i.EventGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        base.OnModelCreating(modelBuilder);
    }

    private static byte[] FloatArrayToBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToFloatArray(byte[] bytes)
    {
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}
