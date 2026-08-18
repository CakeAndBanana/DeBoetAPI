using System.Text;
using DeBoetAPI.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DeBoetAPI.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<CategoryLink> CategoryLinks => Set<CategoryLink>();

    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        });

        builder.Entity<RefreshToken>(e =>
        {
            e.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);
            e.HasOne(t => t.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Item>(e =>
        {
            e.Property(i => i.Name).HasMaxLength(200).IsRequired();
            e.Property(i => i.Description).HasMaxLength(2000);
            e.HasIndex(i => new { i.OwnerId, i.CreatedAt });
            e.HasOne(i => i.Owner)
                .WithMany()
                .HasForeignKey(i => i.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200).IsRequired();
            e.Property(c => c.Slug).HasMaxLength(200).IsRequired();
            e.Property(c => c.PathSlug).IsRequired();
            e.Property(c => c.MetaTitle).HasMaxLength(200);
            e.Property(c => c.MetaDescription).HasMaxLength(2000);
            e.Property(c => c.Path).IsRequired();
            e.HasIndex(c => c.PathSlug).IsUnique();
            e.HasIndex(c => c.Slug);
            e.HasIndex(c => new { c.ParentId, c.SortOrder });
            e.HasIndex(c => c.IsActive);

            e.HasIndex(c => c.Path).HasOperators("text_pattern_ops");

            e.HasOne(c => c.Parent)
                .WithMany(c => c.Children)
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CategoryLink>(e =>
        {
            e.HasKey(l => new { l.ParentId, l.ChildId });

            e.HasOne(l => l.Parent)
                .WithMany()
                .HasForeignKey(l => l.ParentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(l => l.Child)
                .WithMany(c => c.SecondaryParentLinks)
                .HasForeignKey(l => l.ChildId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(l => l.ChildId);
        });

        builder.Entity<ItemCategory>(e =>
        {
            e.HasKey(ic => new { ic.ItemId, ic.CategoryId });

            e.HasOne(ic => ic.Item)
                .WithMany(i => i.Categories)
                .HasForeignKey(ic => ic.ItemId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ic => ic.Category)
                .WithMany()
                .HasForeignKey(ic => ic.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(ic => ic.CategoryId);

            // One primary per item, enforced by the database rather than by convention.
            e.HasIndex(ic => ic.ItemId)
                .IsUnique()
                .HasFilter("is_primary");
        });

        builder.Entity<AuditEntry>(e =>
        {
            e.Property(a => a.EntityName).HasMaxLength(200).IsRequired();
            e.Property(a => a.EntityPath).HasMaxLength(400);
            e.Property(a => a.ChangedByName).HasMaxLength(256).IsRequired();
            e.Property(a => a.Summary).HasMaxLength(4000);

            e.Property(a => a.EntityType).HasConversion<string>().HasMaxLength(40);
            e.Property(a => a.Action).HasConversion<string>().HasMaxLength(40);

            // "History for this record", the most common query.
            e.HasIndex(a => new { a.EntityType, a.EntityId, a.ChangedAt });

            // "Everything, newest first" for the global feed.
            e.HasIndex(a => a.ChangedAt);

            // "Everything this person touched".
            e.HasIndex(a => a.ChangedById);
        });

        ApplySnakeCaseNames(builder);
    }

    private static void ApplySnakeCaseNames(ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName is not null)
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var previousIsLower = i > 0 && char.IsLower(name[i - 1]);
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (i > 0 && name[i - 1] != '_' && (previousIsLower || nextIsLower))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
