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
    public DbSet<CharacterTalent> CharacterTalents => Set<CharacterTalent>();
    public DbSet<CharacterSkill> CharacterSkills => Set<CharacterSkill>();
    public DbSet<CharacterAura> CharacterAuras => Set<CharacterAura>();
    public DbSet<CharacterActionButton> ActionButtons => Set<CharacterActionButton>();
    public DbSet<AccountDataBlob> AccountDataBlobs => Set<AccountDataBlob>();
    public DbSet<TeleportLocation> TeleportLocations => Set<TeleportLocation>();
    public DbSet<SpellTestSession> SpellTestSessions => Set<SpellTestSession>();
    public DbSet<SpellTestResult> SpellTestResults => Set<SpellTestResult>();
    public DbSet<SpellTestRequest> SpellTestRequests => Set<SpellTestRequest>(); // QA T1: очередь запросов на авто-прогон харнесса
    public DbSet<ServerSetting> ServerSettings => Set<ServerSetting>();
    public DbSet<GroupData> Groups => Set<GroupData>(); // GROUP.T6
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>(); // GROUP.T6

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(e =>
        {
            e.ToTable("account");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            // M8.2: логин = email, поэтому username расширен до 255 (было 32).
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(255).IsRequired();
            // M8: email для входа на сайт. Nullable (у игровых/CLI-аккаунтов его нет); уникальный
            // (в MySQL уникальный индекс допускает несколько NULL).
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(255);
            e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("uk_account_email");
            e.Property(x => x.Salt).HasColumnName("salt").HasColumnType("binary(32)").IsRequired();
            e.Property(x => x.Verifier).HasColumnName("verifier").HasColumnType("binary(32)").IsRequired();
            e.Property(x => x.SessionKey).HasColumnName("session_key").HasColumnType("binary(40)");
            e.Property(x => x.LastIp).HasColumnName("last_ip").HasMaxLength(45);
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp")
                .HasDefaultValueSql("CURRENT_TIMESTAMP").ValueGeneratedOnAdd();
            e.Property(x => x.IsAdmin).HasColumnName("is_admin").HasDefaultValue((byte)0);
            e.HasIndex(x => x.Username).IsUnique().HasDatabaseName("uk_account_username");
        });

        modelBuilder.Entity<Realm>(e =>
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

        modelBuilder.Entity<Character>(e =>
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
            e.Property(x => x.TalentResetCost).HasColumnName("talent_reset_cost").HasDefaultValue(0u);
            e.Property(x => x.IsTester).HasColumnName("is_tester").HasDefaultValue(false); // KB6: QA-тестировщик
            // KB#87: soft-delete. NULL = живой персонаж, DateTime = момент удаления.
            e.Property(x => x.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamp");
            // Composite UNIQUE (name, deleted_at): MySQL трактует NULL в unique-индексе как distinct,
            // поэтому один живой «Опал» сосуществует с N удалёнными «Опал» с разными timestamp.
            e.HasIndex(x => new { x.Name, x.DeletedAt }).IsUnique().HasDatabaseName("uk_characters_name");
            e.HasIndex(x => x.AccountId).HasDatabaseName("ix_characters_account");
        });

        modelBuilder.Entity<CharacterItem>(e =>
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

        modelBuilder.Entity<DeclinedName>(e =>
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

        modelBuilder.Entity<CharacterQuestStatus>(e =>
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

        modelBuilder.Entity<CharacterSpell>(e =>
        {
            e.ToTable("character_spell");
            e.HasKey(x => new { x.OwnerGuid, x.Spell });
            e.Property(x => x.OwnerGuid).HasColumnName("owner_guid");
            e.Property(x => x.Spell).HasColumnName("spell");
            e.HasIndex(x => x.OwnerGuid).HasDatabaseName("ix_spell_owner");
        });

        modelBuilder.Entity<CharacterTalent>(e =>
        {
            e.ToTable("character_talent");
            e.HasKey(x => new { x.OwnerGuid, x.TalentId });
            e.Property(x => x.OwnerGuid).HasColumnName("owner_guid");
            e.Property(x => x.TalentId).HasColumnName("talent_id");
            e.Property(x => x.Rank).HasColumnName("rank").HasDefaultValue((byte)0);
            e.HasIndex(x => x.OwnerGuid).HasDatabaseName("ix_talent_owner");
        });

        modelBuilder.Entity<CharacterSkill>(e =>
        {
            e.ToTable("character_skill");
            e.HasKey(x => new { x.OwnerGuid, x.SkillId });
            e.Property(x => x.OwnerGuid).HasColumnName("owner_guid");
            e.Property(x => x.SkillId).HasColumnName("skill_id");
            e.Property(x => x.Value).HasColumnName("value").HasDefaultValue((ushort)0);
            e.Property(x => x.Max).HasColumnName("max_value").HasDefaultValue((ushort)0);
            e.Property(x => x.Step).HasColumnName("step").HasDefaultValue((byte)0);
            e.HasIndex(x => x.OwnerGuid).HasDatabaseName("ix_skill_owner");
        });

        modelBuilder.Entity<CharacterAura>(e =>
        {
            e.ToTable("character_aura");
            e.HasKey(x => new { x.OwnerGuid, x.Spell });
            e.Property(x => x.OwnerGuid).HasColumnName("owner_guid");
            e.Property(x => x.Spell).HasColumnName("spell");
            e.Property(x => x.Form).HasColumnName("form").HasDefaultValue((byte)0);
            e.Property(x => x.RemainingMs).HasColumnName("remaining_ms").HasDefaultValue(0u); // M10.5: временны́е баффы
            e.HasIndex(x => x.OwnerGuid).HasDatabaseName("ix_aura_owner");
        });

        modelBuilder.Entity<CharacterActionButton>(e =>
        {
            e.ToTable("character_action");
            e.HasKey(x => new { x.OwnerGuid, x.Button });
            e.Property(x => x.OwnerGuid).HasColumnName("owner_guid");
            e.Property(x => x.Button).HasColumnName("button");
            e.Property(x => x.PackedData).HasColumnName("packed_data");
        });

        modelBuilder.Entity<AccountDataBlob>(e =>
        {
            e.ToTable("account_data");
            e.HasKey(x => new { x.OwnerId, x.IsChar, x.DataType });
            e.Property(x => x.OwnerId).HasColumnName("owner_id");
            e.Property(x => x.IsChar).HasColumnName("is_char");
            e.Property(x => x.DataType).HasColumnName("data_type");
            e.Property(x => x.UpdateTime).HasColumnName("update_time").HasDefaultValue(0u);
            e.Property(x => x.Data).HasColumnName("data").HasColumnType("longblob");
        });

        modelBuilder.Entity<TeleportLocation>(e =>
        {
            e.ToTable("dev_teleport");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
            e.Property(x => x.Faction).HasColumnName("faction").HasDefaultValue((byte)0);
            e.Property(x => x.Map).HasColumnName("map").HasDefaultValue(0u);
            e.Property(x => x.Zone).HasColumnName("zone").HasDefaultValue(0u);
            e.Property(x => x.X).HasColumnName("x").HasDefaultValue(0f);
            e.Property(x => x.Y).HasColumnName("y").HasDefaultValue(0f);
            e.Property(x => x.Z).HasColumnName("z").HasDefaultValue(0f);
            e.Property(x => x.O).HasColumnName("o").HasDefaultValue(0f);
        });

        // M12 Spell QA: захват проверки заклинаний (заголовок сессии + строки результатов).
        modelBuilder.Entity<SpellTestSession>(e =>
        {
            e.ToTable("spell_test_session");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OwnerGuid).HasColumnName("owner_guid");
            e.Property(x => x.AccountId).HasColumnName("account_id");
            e.Property(x => x.Class).HasColumnName("class");
            e.Property(x => x.Level).HasColumnName("level");
            e.Property(x => x.Mode).HasColumnName("mode").HasDefaultValue((byte)0);
            e.Property(x => x.TalentsSlotted).HasColumnName("talents_slotted").HasDefaultValue((byte)0);
            e.Property(x => x.StartedAt).HasColumnName("started_at").HasColumnType("datetime(3)");
            e.Property(x => x.EndedAt).HasColumnName("ended_at").HasColumnType("datetime(3)");
            e.Property(x => x.Note).HasColumnName("note").HasMaxLength(128);
            e.Property(x => x.Analyzed).HasColumnName("analyzed").HasDefaultValue((byte)0);
            e.Property(x => x.TicketId).HasColumnName("ticket_id");
            e.HasIndex(x => x.OwnerGuid).HasDatabaseName("ix_sts_owner");
        });

        modelBuilder.Entity<SpellTestResult>(e =>
        {
            e.ToTable("spell_test_result");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SessionId).HasColumnName("session_id");
            e.Property(x => x.SpellId).HasColumnName("spell_id");
            e.Property(x => x.Class).HasColumnName("class");
            e.Property(x => x.Level).HasColumnName("level");
            e.Property(x => x.ResultType).HasColumnName("result_type");
            e.Property(x => x.School).HasColumnName("school");
            e.Property(x => x.Amount).HasColumnName("amount");
            e.Property(x => x.Effective).HasColumnName("effective");
            e.Property(x => x.OverkillOrOverheal).HasColumnName("overkill_or_overheal");
            e.Property(x => x.ExpectedMin).HasColumnName("expected_min");
            e.Property(x => x.ExpectedMax).HasColumnName("expected_max");
            e.Property(x => x.ExpectedCost).HasColumnName("expected_cost");
            e.Property(x => x.PowerType).HasColumnName("power_type");
            e.Property(x => x.IsHeal).HasColumnName("is_heal");
            e.Property(x => x.WeaponBased).HasColumnName("weapon_based");
            e.Property(x => x.FamilyName).HasColumnName("family_name");
            e.Property(x => x.CastIndex).HasColumnName("cast_index");
            e.Property(x => x.RecordedAt).HasColumnName("recorded_at").HasColumnType("datetime(3)");
            e.HasIndex(x => x.SessionId).HasDatabaseName("ix_str_session");
            e.HasIndex(x => x.SpellId).HasDatabaseName("ix_str_spell");
        });

        // QA T1: очередь внешних запросов на авто-прогон харнесса. Web/Claude вставляет
        // (status=pending), World-tick подхватывает (CAS pending→running) и финализирует (done/failed).
        modelBuilder.Entity<SpellTestRequest>(e =>
        {
            e.ToTable("spell_test_request");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Account).HasColumnName("account").HasMaxLength(32).IsRequired();
            e.Property(x => x.Casts).HasColumnName("casts");
            e.Property(x => x.Note).HasColumnName("note").HasMaxLength(128);
            e.Property(x => x.Status).HasColumnName("status").HasDefaultValue((byte)0);
            e.Property(x => x.SessionId).HasColumnName("session_id");
            e.Property(x => x.Error).HasColumnName("error").HasMaxLength(255);
            e.Property(x => x.RequestedAt).HasColumnName("requested_at").HasColumnType("datetime(3)");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at").HasColumnType("datetime(3)");
            // World-tick ищет невыполненные — индекс по статусу + времени (FIFO).
            e.HasIndex(x => new { x.Status, x.RequestedAt }).HasDatabaseName("ix_strq_status");
        });

        // Key-value настройки сервера (M8.6: стоимость смены расы/пола и т.п.).
        modelBuilder.Entity<ServerSetting>(e =>
        {
            e.ToTable("server_setting");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasColumnName("setting_key").HasMaxLength(64).ValueGeneratedNever();
            e.Property(x => x.Value).HasColumnName("setting_value").HasMaxLength(255).IsRequired();
        });

        // GROUP.T6: persistence группы. group_data + group_member.
        modelBuilder.Entity<GroupData>(e =>
        {
            e.ToTable("group_data");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.LeaderGuid).HasColumnName("leader_guid").IsRequired();
            e.Property(x => x.LeaderName).HasColumnName("leader_name").HasMaxLength(32).IsRequired();
            e.Property(x => x.Type).HasColumnName("type").HasDefaultValue((byte)0);
            e.Property(x => x.LootMethod).HasColumnName("loot_method").HasDefaultValue((byte)0);
            e.Property(x => x.LootMasterGuid).HasColumnName("loot_master_guid").HasDefaultValue((uint)0);
        });

        modelBuilder.Entity<GroupMember>(e =>
        {
            e.ToTable("group_member");
            e.HasKey(x => new { x.GroupId, x.CharGuid });
            e.Property(x => x.GroupId).HasColumnName("group_id");
            e.Property(x => x.CharGuid).HasColumnName("char_guid");
            e.Property(x => x.SubGroup).HasColumnName("subgroup").HasDefaultValue((byte)0);
            e.Property(x => x.IsAssistant).HasColumnName("is_assistant").HasDefaultValue(false);
            e.HasIndex(x => x.CharGuid).HasDatabaseName("ix_gm_char");
        });
    }
}
