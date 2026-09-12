using Looxdex.Api.Data;
using Looxdex.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Looxdex.Api.Services;

/// <summary>
/// Gives a new wearer a small starter closet so the app has something to work
/// with before they add anything of their own.
///
/// Seeding is recorded against the owner rather than inferred from an empty
/// closet: otherwise deleting every item would quietly bring the starters back.
/// </summary>
public class StarterClosetService
{
    private readonly LooxdexDbContext _db;
    private readonly ILogger<StarterClosetService> _logger;

    public StarterClosetService(LooxdexDbContext db, ILogger<StarterClosetService> logger)
    {
        _db = db;
        _logger = logger;
    }

    private static readonly (string Name, string Category, string Color, string Hex, string Season, string Brand, string Formality, string Image)[] Starters =
    {
        ("חולצת פשתן לבנה", "חולצות", "לבן", "#F7F5F0", "קיץ", "Zara", "יומיומי",
            "https://images.unsplash.com/photo-1596755094514-f87e34085b2c?w=600&q=80"),
        ("ג'ינס מחויט כחול", "מכנסיים", "כחול", "#3B5A7A", "כל השנה", "Levi's", "יומיומי",
            "https://images.unsplash.com/photo-1541099649105-f69ad21f3246?w=600&q=80"),
        ("בלייזר בז' מחויט", "ז׳קטים", "בז'", "#C9B896", "אביב", "Mango", "אלגנט",
            "https://images.unsplash.com/photo-1591047139829-d91aecb6caea?w=600&q=80"),
        ("סניקרס לבנים", "נעליים", "לבן", "#F7F5F0", "כל השנה", "Common Projects", "יומיומי",
            "https://images.unsplash.com/photo-1560769629-975ec94e6a86?w=600&q=80"),
        ("תיק צד עור קרם", "תיקים", "קרם", "#EFE6D8", "כל השנה", "Mansur Gavriel", "יומיומי",
            "https://images.unsplash.com/photo-1584917865442-de89df76afd3?w=600&q=80")
    };

    /// <summary>
    /// Seeds the starter closet the first time an owner is seen. Safe to call on
    /// every request; it does nothing once the owner has been recorded.
    /// </summary>
    public async Task EnsureSeededAsync(string ownerKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerKey)) return;

        var profile = await _db.OwnerProfiles
            .FirstOrDefaultAsync(p => p.OwnerKey == ownerKey, ct);

        if (profile?.SeededAt is not null) return;

        // The demo owner already has the full hand-authored closet.
        if (ownerKey == HeaderOwnerContext.DemoOwnerKey)
        {
            await RecordSeededAsync(profile, ownerKey, ct);
            return;
        }

        var now = DateTime.UtcNow;
        var items = Starters.Select((s, index) => new ClosetItemEntity
        {
            OwnerKey = ownerKey,
            Name = s.Name,
            ImageUrl = s.Image,
            Category = s.Category,
            Color = s.Color,
            ColorHex = s.Hex,
            Season = s.Season,
            Brand = s.Brand,
            Formality = s.Formality,
            // Stagger so "newest first" keeps the intended order.
            AddedAt = now.AddSeconds(-index)
        });

        _db.ClosetItems.AddRange(items);
        await RecordSeededAsync(profile, ownerKey, ct);

        _logger.LogInformation(
            "Seeded {Count} starter items for a new owner.", Starters.Length);
    }

    private async Task RecordSeededAsync(
        OwnerProfileEntity? profile, string ownerKey, CancellationToken ct)
    {
        if (profile is null)
        {
            _db.OwnerProfiles.Add(new OwnerProfileEntity
            {
                OwnerKey = ownerKey,
                FirstSeenAt = DateTime.UtcNow,
                SeededAt = DateTime.UtcNow
            });
        }
        else
        {
            profile.SeededAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}
