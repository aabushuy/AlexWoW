using AlexWoW.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlexWoW.Database;

/// <summary>
/// EF Core контекст нашей изменяемой БД <c>alexwow_auth</c> (аккаунты, реалмы, персонажи и их состояние).
/// Срез 2 рефактора DAL (#23): схема описана Fluent-конфигурацией ТОЧНО под текущую прод-схему, чтобы
/// миграция <c>InitialCreate</c> совпадала с живой БД (прод адаптируется baseline-строкой в
/// <c>__EFMigrationsHistory</c>, без пересоздания таблиц). На рантайме пока не используется — данные
/// по-прежнему обслуживает Dapper; EF включится в следующих срезах.
/// </summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Realm> Realms => Set<Realm>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CharacterItem> CharacterItems => Set<CharacterItem>();
    public DbSet<DeclinedName> DeclinedNames => Set<DeclinedName>();
    public DbSet<CharacterQuestStatus> QuestStatuses => Set<CharacterQuestStatus>();
    public DbSet<CharacterSpell> CharacterSpells => Set<CharacterSpell>();
    public DbSet<CharacterAura> CharacterAuras => Set<CharacterAura>();
    public DbSet<CharacterActionButton> ActionButtons => Set<CharacterActionButton>();
    public DbSet<AccountDataBlob> AccountDataBlobs => Set<AccountDataBlob>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Account>(e =>
        {
            e.ToTable("account");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(32).IsRequired();
            e.Property(x => x.Salt).HasColumnName("salt").HasColumnType("binary(32)").IsRequired();
            e.Property(x => x.Verifier).HasColumnName("verifier").HasColumnType("binary(32)").IsRequired();
            e.Property(x => x.SessionKey).HasColumnName("session_key").HasColumnType("binary(40)");
            e.Property(x => x.LastIp).HasColumnName("last_ip").HasMaxLength(45);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp")
                .HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
            e.Property(x => x.IsAdmin).HasColumnName("is_admin").HasDefaultValue((byte)0);
            e.HasIndex(x => x.Username).IsUnique().HasDatabaseName("uk_account_username");
        });

        b.Entity<Realm>(e =>
        {
            e.ToTable("realmlist");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
            e.Property(x => x.Address).HasColumnName("address").HasMaxLength(64).IsRequired();
            e.Property(x => x.Port).HasColumnName("port").HasDefaultValue((ushort)8085);
            e.Property(x => x.Type).HasColumnName("type").HasDefaultValue((byte)0);
            e.Property(x => x.Flags).HasColumnName("flags").HasDefaultValue((byte)0);
            e.Property(x => x.Timezone).HasColumnName("timezone").HasDefaultValue((byte)1);
            e.Property(x => x.Population).HasColumnName("population").HasDefaultValue(0f);
            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("uk_realmlist_name");
        });

        b.Entity<Character>(e =>
        {
            e.ToTable("characters");
            e.HasKey(x => x.Guid);
            e.Property(x => x.Guid).HasColumnName("guid");
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(12).IsRequired();
            e.Property(x => x.Race).HasColumnName("race");
            e.Property(x => x.Class).HasColumnName("class");
            e.Property(x => x.Gender).HasColumnName("gender");
            e.Property(x => x.Skin).HasColumnName("skin");
            e.Property(x => x.Face).HasColumnName("face");
            e.Property(x => x.HairStyle).HasColumnName("hair_style");
            e.Property(x => x.HairColor).HasColumnName("hair_color");
            e.Property(x => x.FacialHair).HasColumnName("facial_hair");
            e.Property(x => x.Level).HasColumnName("level").HasDefaultValue((byte)1);
            e.Property(x => x.Zone).HasColumnName("zone").HasDefaultValue(0u);
            e.Property(x => x.Map).HasColumnName("map").HasDefaultValue(0u);
            e.Property(x => x.PositionX).HasColumnName("position_x").HasDefaultValue(0f);
            e.Property(x => x.PositionY).HasColumnName("position_y").HasDefaultValue(0f);
            e.Property(x => x.PositionZ).HasColumnName("position_z").HasDefaultValue(0f);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp")
                .HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
            e.Property(x => x.Money).HasColumnName("money").HasDefaultValue(1000000u);
            e.Property(x => x.Xp).HasColumnName("xp").HasDefaultValue(0u);
            e.Property(x => x.ActionBars).HasColumnName("action_bars").HasDefaultValue((byte)0);
            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("uk_characters_name");
            e.HasIndex(x => x.AccountId).HasDatabaseName("ix_characters_account");
        });

        b.Entity<CharacterItem>(e =>
        {
            e.ToTable("character_items");
            e.HasKey(x => x.ItemGuid);
            e.Property(x => x.ItemGuid).HasColumnName("item_guid");
            e.Property(x => x.OwnerGuid).HasColumnName("owner_guid");
            e.Property(x => x.ItemEntry).HasColumnName("item_entry");
            e.Property(x => x.Bag).HasColumnName("bag").HasDefaultValue((byte)255);
            e.Property(x => x.Slot).HasColumnName("slot");
            e.Property(x => x.StackCount).HasColumnName("stack_count").HasDefaultValue(1u);
            e.HasIndex(x => x.OwnerGuid).HasDatabaseName("ix_items_owner");
        });

        b.Entity<DeclinedName>(e =>
        {
            e.ToTable("character_declined_names");
            e.HasKey(x => x.OwnerGuid);
            e.Property(x => x.OwnerGuid).HasColumnName("owner_guid").ValueGeneratedNever();
            e.Property(x => x.N0).HasColumnName("n0").HasMaxLength(24).HasDefaultValue("").IsRequired();
            e.Property(x => x.N1).HasColumnName("n1").HasMaxLength(24).HasDefaultValue("").IsRequired();
            e.Property(x => x.N2).HasColumnName("n2").HasMaxLength(24).HasDefaultValue("").IsRequired();
            e.Property(x => x.N3).HasColumnName("n3").HasMaxLength(24).HasDefaultValue("").IsRequired();
            e.Property(x => x.N4).HasColumnName("n4").HasMaxLength(24).HasDefaultValue("").IsRequired();
        });

        b.Entity<CharacterQuestStatus>(e =>
        {
            e.ToTable("character_queststatus");
            e.HasKey(x => new { x.OwnerGuid, x.QuestId });
            e.Property(x => x.OwnerGuid).HasColumnName("owner_guid");
            e.Property(x => x.QuestId).HasColumnName("quest_id");
            e.Property(x => x.Slot).HasColumnName("slot").HasDefaultValue((byte)0);
            e.Property(x => x.Status).HasColumnName("status").HasDefaultValue((byte)0);
            e.Property(x => x.Counter0).HasColumnName("counter0").HasDefaultValue((ushort)0);
            e.Property(x => x.Counter1).HasColumnName("counter1").HasDefaultValue((ushort)0);
            e.Property(x => x.Counter2).HasColumnName("counter2").HasDefaultValue((ushort)0);
            e.Property(x => x.Counter3).HasColumnName("counter3").HasDefaultValue((ushort)0);
            e.HasIndex(x => x.OwnerGuid).HasDatabaseName("ix_qs_owner");
        });

        b.Entity<CharacterSpell>(e =>
        {
            e.ToTable("character_spell");
            e.HasKey(x => new { x.OwnerGuid, x.Spell });
            e.Property(x => x.OwnerGuid).HasColumnName("owner_guid");
            e.Property(x => x.Spell).HasColumnName("spell");
            e.HasIndex(x => x.OwnerGuid).HasDatabaseName("ix_spell_owner");
        });

        b.Entity<CharacterAura>(e =>
        {
            e.ToTable("character_aura");
            e.HasKey(x => new { x.OwnerGuid, x.Spell });
            e.Property(x => x.OwnerGuid).HasColumnName("owner_guid");
            e.Property(x => x.Spell).HasColumnName("spell");
            e.Property(x => x.Form).HasColumnName("form").HasDefaultValue((byte)0);
            e.Property(x => x.RemainingMs).HasColumnName("remaining_ms").HasDefaultValue(0u); // M10.5: временны́е баффы
            e.HasIndex(x => x.OwnerGuid).HasDatabaseName("ix_aura_owner");
        });

        b.Entity<CharacterActionButton>(e =>
        {
            e.ToTable("character_action");
            e.HasKey(x => new { x.OwnerGuid, x.Button });
            e.Property(x => x.OwnerGuid).HasColumnName("owner_guid");
            e.Property(x => x.Button).HasColumnName("button");
            e.Property(x => x.PackedData).HasColumnName("packed_data");
        });

        b.Entity<AccountDataBlob>(e =>
        {
            e.ToTable("account_data");
            e.HasKey(x => new { x.OwnerId, x.IsChar, x.DataType });
            e.Property(x => x.OwnerId).HasColumnName("owner_id");
            e.Property(x => x.IsChar).HasColumnName("is_char");
            e.Property(x => x.DataType).HasColumnName("data_type");
            e.Property(x => x.UpdateTime).HasColumnName("update_time").HasDefaultValue(0u);
            e.Property(x => x.Data).HasColumnName("data").HasColumnType("longblob");
        });
    }
}
