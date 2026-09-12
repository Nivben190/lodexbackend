using Looxdex.Api.Entities;
using Looxdex.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Looxdex.Api.Data;

/// <summary>
/// Moves the original hardcoded demo content into the database on first boot.
///
/// The closet and suitcases are seeded against the demo owner so the app has
/// something to show before a user adds anything. Feed posts now come from the
/// ingest pipeline, so none are seeded here.
/// </summary>
public class DatabaseSeeder
{
    private readonly LooxdexDbContext _db;
    private readonly LooxdexSeedData _seed;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        LooxdexDbContext db,
        LooxdexSeedData seed,
        ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _seed = seed;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct)
    {
        var owner = HeaderOwnerContext.DemoOwnerKey;

        if (!await _db.ClosetItems.AnyAsync(ct))
        {
            _db.ClosetItems.AddRange(_seed.ClosetItems.Select(i => new ClosetItemEntity
            {
                // No explicit Id: assigning one leaves Postgres's identity
                // sequence at its start, so the next insert collides on the
                // primary key. Nothing references these ids any more.
                OwnerKey = owner,
                Name = i.Name,
                ImageUrl = i.ImageUrl,
                Category = i.Category,
                Color = i.Color,
                ColorHex = i.ColorHex,
                Season = i.Season,
                Brand = i.Brand,
                Formality = i.Formality,
                IsFavorite = i.IsFavorite,
                AddedAt = i.AddedAt
            }));

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} closet items.", _seed.ClosetItems.Count);
        }

        // Suitcases are no longer part of the product, so they are not seeded.
        // The tables remain for now; removing them is a separate migration.
    }
}
