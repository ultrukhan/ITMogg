using Npgsql;
using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Models;

namespace RoomieSync.Data.Postgres;


public sealed class PgHouseholdRepository(IDbConnectionFactory db) : IHouseholdRepository
{
    public async Task<Household?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("SELECT id, name, created_at FROM households WHERE id = @id").P("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new Household { Id = r.GetGuid(0), Name = r.GetString(1), CreatedAt = r.GetDateTime(2) } : null;
    }

    public async Task<Household?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("SELECT id, name, created_at FROM households WHERE lower(name) = lower(@name)").P("name", name.Trim());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new Household { Id = r.GetGuid(0), Name = r.GetString(1), CreatedAt = r.GetDateTime(2) } : null;
    }

    public async Task<IReadOnlyList<Household>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("SELECT id, name, created_at FROM households ORDER BY created_at");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<Household>();
        while (await r.ReadAsync(ct))
            list.Add(new Household { Id = r.GetGuid(0), Name = r.GetString(1), CreatedAt = r.GetDateTime(2) });
        return list;
    }

    public async Task<bool> AnyAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("SELECT EXISTS (SELECT 1 FROM households)");
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task AddAsync(Household h, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("INSERT INTO households (id, name, created_at) VALUES (@id, @name, @created)")
            .P("id", h.Id).P("name", h.Name).P("created", h.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(Household h, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("UPDATE households SET name = @name WHERE id = @id").P("id", h.Id).P("name", h.Name);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class PgMemberRepository(IDbConnectionFactory db) : IMemberRepository
{
    private const string Columns = "id, household_id, display_name, role, avatar_color, joined_at, is_active";

    private static Member Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        HouseholdId = r.GetGuid(1),
        DisplayName = r.GetString(2),
        Role = (MemberRole)r.GetInt16(3),
        AvatarColor = r.GetString(4),
        JoinedAt = r.GetDateTime(5),
        IsActive = r.GetBoolean(6)
    };

    public async Task<IReadOnlyList<Member>> GetByHouseholdAsync(Guid householdId, bool includeInactive = false, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd($"SELECT {Columns} FROM members WHERE household_id = @h AND (@all OR is_active) ORDER BY joined_at")
            .P("h", householdId).P("all", includeInactive);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<Member>();
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<Member?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd($"SELECT {Columns} FROM members WHERE id = @id").P("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task AddAsync(Member m, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd($"INSERT INTO members ({Columns}) VALUES (@id, @h, @name, @role, @color, @joined, @active)")
            .P("id", m.Id).P("h", m.HouseholdId).P("name", m.DisplayName).P("role", (short)m.Role)
            .P("color", m.AvatarColor).P("joined", m.JoinedAt).P("active", m.IsActive);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(Member m, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("UPDATE members SET display_name = @name, role = @role, avatar_color = @color, is_active = @active WHERE id = @id")
            .P("id", m.Id).P("name", m.DisplayName).P("role", (short)m.Role).P("color", m.AvatarColor).P("active", m.IsActive);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class PgUserAccountRepository(IDbConnectionFactory db) : IUserAccountRepository
{
    private const string Columns = "member_id, email, password_hash, password_salt, failed_attempts, locked_until";

    private static UserAccount Map(NpgsqlDataReader r) => new()
    {
        MemberId = r.GetGuid(0),
        Email = r.GetString(1),
        PasswordHash = r.GetString(2),
        PasswordSalt = r.GetString(3),
        FailedAttempts = r.GetInt32(4),
        LockedUntil = r.IsDBNull(5) ? null : r.GetDateTime(5)
    };

    public async Task<UserAccount?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd($"SELECT {Columns} FROM user_accounts WHERE lower(email) = lower(@e)").P("e", email.Trim());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task<IReadOnlyList<UserAccount>> GetAllByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd($"SELECT {Columns} FROM user_accounts WHERE lower(email) = lower(@e)").P("e", email.Trim());
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<UserAccount>();
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<UserAccount?> GetByMemberIdAsync(Guid memberId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd($"SELECT {Columns} FROM user_accounts WHERE member_id = @m").P("m", memberId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task AddAsync(UserAccount a, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using (var dropIdx = conn.Cmd("DROP INDEX IF EXISTS ux_user_accounts_email;"))
            await dropIdx.ExecuteNonQueryAsync(ct);

        await using var cmd = conn.Cmd(
            $"INSERT INTO user_accounts ({Columns}) VALUES (@m, @e, @h, @s, @f, @l)")
            .P("m", a.MemberId)
            .P("e", a.Email.Trim())
            .P("h", a.PasswordHash)
            .P("s", a.PasswordSalt)
            .P("f", a.FailedAttempts)
            .P("l", a.LockedUntil);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(UserAccount a, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("""
            UPDATE user_accounts
               SET email = @e, password_hash = @h, password_salt = @s, failed_attempts = @f, locked_until = @l
             WHERE member_id = @m
            """)
            .P("m", a.MemberId).P("e", a.Email.Trim()).P("h", a.PasswordHash).P("s", a.PasswordSalt)
            .P("f", a.FailedAttempts).P("l", a.LockedUntil);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class PgExpenseRepository(IDbConnectionFactory db) : IExpenseRepository
{
    private const string Columns = "id, household_id, title, merchant, amount, expense_date, category, paid_by_member_id, split_method, receipt_image_path, created_at";

    private static Expense Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        HouseholdId = r.GetGuid(1),
        Title = r.GetString(2),
        Merchant = r.IsDBNull(3) ? null : r.GetString(3),
        Amount = r.GetDecimal(4),
        Date = r.GetDateTime(5),
        Category = (ExpenseCategory)r.GetInt16(6),
        PaidByMemberId = r.GetGuid(7),
        SplitMethod = (SplitMethod)r.GetInt16(8),
        ReceiptImagePath = r.IsDBNull(9) ? null : r.GetString(9),
        CreatedAt = r.GetDateTime(10)
    };

    public async Task<IReadOnlyList<Expense>> GetByHouseholdAsync(Guid householdId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        var list = new List<Expense>();
        await using (var cmd = conn.Cmd($"""
            SELECT {Columns} FROM expenses
             WHERE household_id = @h
               AND (@from::date IS NULL OR expense_date >= @from::date)
               AND (@to::date   IS NULL OR expense_date <= @to::date)
             ORDER BY expense_date DESC, created_at DESC
            """).P("h", householdId).P("from", from).P("to", to))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) list.Add(Map(r));

        if (list.Count == 0) return list;
        var byId = list.ToDictionary(e => e.Id);
        await using (var cmd = conn.Cmd("""
            SELECT s.expense_id, s.member_id, s.amount, s.percentage
              FROM expense_shares s JOIN expenses e ON e.id = s.expense_id
             WHERE e.household_id = @h
               AND (@from::date IS NULL OR e.expense_date >= @from::date)
               AND (@to::date   IS NULL OR e.expense_date <= @to::date)
            """).P("h", householdId).P("from", from).P("to", to))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                if (byId.TryGetValue(r.GetGuid(0), out var e))
                    e.Shares.Add(new ExpenseShare { MemberId = r.GetGuid(1), Amount = r.GetDecimal(2), Percentage = r.IsDBNull(3) ? null : r.GetDecimal(3) });
        return list;
    }

    public async Task<Expense?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        Expense? e;
        await using (var cmd = conn.Cmd($"SELECT {Columns} FROM expenses WHERE id = @id").P("id", id))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            e = await r.ReadAsync(ct) ? Map(r) : null;
        if (e is null) return null;
        await using (var cmd = conn.Cmd("SELECT member_id, amount, percentage FROM expense_shares WHERE expense_id = @id").P("id", id))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                e.Shares.Add(new ExpenseShare { MemberId = r.GetGuid(0), Amount = r.GetDecimal(1), Percentage = r.IsDBNull(2) ? null : r.GetDecimal(2) });
        return e;
    }

    public async Task AddAsync(Expense e, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = conn.Cmd($"INSERT INTO expenses ({Columns}) VALUES (@id, @h, @t, @m, @a, @d, @c, @p, @s, @r, @cr)", tx)
            .P("id", e.Id).P("h", e.HouseholdId).P("t", e.Title).P("m", e.Merchant).P("a", e.Amount).P("d", e.Date.Date)
            .P("c", (short)e.Category).P("p", e.PaidByMemberId).P("s", (short)e.SplitMethod).P("r", e.ReceiptImagePath).P("cr", e.CreatedAt))
            await cmd.ExecuteNonQueryAsync(ct);

        foreach (var s in e.Shares)
        {
            await using var cmd = conn.Cmd("INSERT INTO expense_shares (expense_id, member_id, amount, percentage) VALUES (@e, @m, @a, @p)", tx)
                .P("e", e.Id).P("m", s.MemberId).P("a", s.Amount).P("p", s.Percentage);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task UpdateAsync(Expense e, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var cmd = conn.Cmd("""
            UPDATE expenses
               SET title = @t, merchant = @m, amount = @a, expense_date = @d,
                   category = @c, paid_by_member_id = @p, split_method = @s, receipt_image_path = @r
             WHERE id = @id
            """, tx)
            .P("id", e.Id).P("t", e.Title).P("m", e.Merchant).P("a", e.Amount).P("d", e.Date.Date)
            .P("c", (short)e.Category).P("p", e.PaidByMemberId).P("s", (short)e.SplitMethod).P("r", e.ReceiptImagePath))
            await cmd.ExecuteNonQueryAsync(ct);

        await using (var delCmd = conn.Cmd("DELETE FROM expense_shares WHERE expense_id = @id", tx).P("id", e.Id))
            await delCmd.ExecuteNonQueryAsync(ct);

        foreach (var s in e.Shares)
        {
            await using var cmd = conn.Cmd("INSERT INTO expense_shares (expense_id, member_id, amount, percentage) VALUES (@e, @m, @a, @p)", tx)
                .P("e", e.Id).P("m", s.MemberId).P("a", s.Amount).P("p", s.Percentage);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("DELETE FROM expenses WHERE id = @id").P("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class PgSettlementRepository(IDbConnectionFactory db) : ISettlementRepository
{
    public async Task<IReadOnlyList<Settlement>> GetByHouseholdAsync(Guid householdId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("""
            SELECT id, household_id, from_member_id, to_member_id, amount, settled_at
              FROM settlements WHERE household_id = @h ORDER BY settled_at DESC
            """).P("h", householdId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<Settlement>();
        while (await r.ReadAsync(ct))
            list.Add(new Settlement
            {
                Id = r.GetGuid(0),
                HouseholdId = r.GetGuid(1),
                FromMemberId = r.GetGuid(2),
                ToMemberId = r.GetGuid(3),
                Amount = r.GetDecimal(4),
                Date = r.GetDateTime(5)
            });
        return list;
    }

    public async Task AddAsync(Settlement s, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("""
            INSERT INTO settlements (id, household_id, from_member_id, to_member_id, amount, settled_at)
            VALUES (@id, @h, @f, @t, @a, @d)
            """).P("id", s.Id).P("h", s.HouseholdId).P("f", s.FromMemberId).P("t", s.ToMemberId).P("a", s.Amount).P("d", s.Date);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class PgChoreRepository(IDbConnectionFactory db) : IChoreRepository
{
    private const string Columns = "id, household_id, title, assignee_id, deadline, status, rotation, created_at, completed_at";

    private static Chore Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        HouseholdId = r.GetGuid(1),
        Title = r.GetString(2),
        AssigneeId = r.IsDBNull(3) ? null : r.GetGuid(3),
        Deadline = r.IsDBNull(4) ? null : r.GetDateTime(4),
        Status = (ChoreStatus)r.GetInt16(5),
        Rotation = (ChoreRotation)r.GetInt16(6),
        CreatedAt = r.GetDateTime(7),
        CompletedAt = r.IsDBNull(8) ? null : r.GetDateTime(8)
    };

    public async Task<IReadOnlyList<Chore>> GetByHouseholdAsync(Guid householdId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd($"SELECT {Columns} FROM chores WHERE household_id = @h ORDER BY created_at").P("h", householdId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<Chore>();
        while (await r.ReadAsync(ct)) list.Add(Map(r));
        return list;
    }

    public async Task<Chore?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd($"SELECT {Columns} FROM chores WHERE id = @id").P("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Map(r) : null;
    }

    public async Task AddAsync(Chore c, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd($"INSERT INTO chores ({Columns}) VALUES (@id, @h, @t, @a, @d, @s, @r, @c, @done)")
            .P("id", c.Id).P("h", c.HouseholdId).P("t", c.Title).P("a", c.AssigneeId).P("d", c.Deadline)
            .P("s", (short)c.Status).P("r", (short)c.Rotation).P("c", c.CreatedAt).P("done", c.CompletedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(Chore c, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("""
            UPDATE chores SET title = @t, assignee_id = @a, deadline = @d, status = @s, rotation = @r, completed_at = @done
             WHERE id = @id
            """)
            .P("id", c.Id).P("t", c.Title).P("a", c.AssigneeId).P("d", c.Deadline)
            .P("s", (short)c.Status).P("r", (short)c.Rotation).P("done", c.CompletedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("DELETE FROM chores WHERE id = @id").P("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class PgNotificationRepository(IDbConnectionFactory db) : INotificationRepository
{
    public async Task<IReadOnlyList<Notification>> GetForMemberAsync(Guid memberId, int take = 50, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("""
            SELECT id, household_id, recipient_member_id, type, message, created_at, is_read
              FROM notifications WHERE recipient_member_id = @m
             ORDER BY created_at DESC LIMIT @take
            """).P("m", memberId).P("take", take);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<Notification>();
        while (await r.ReadAsync(ct))
            list.Add(new Notification
            {
                Id = r.GetGuid(0),
                HouseholdId = r.GetGuid(1),
                RecipientMemberId = r.GetGuid(2),
                Type = (NotificationType)r.GetInt16(3),
                Message = r.GetString(4),
                CreatedAt = r.GetDateTime(5),
                IsRead = r.GetBoolean(6)
            });
        return list;
    }

    public async Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var n in notifications)
        {
            await using var cmd = conn.Cmd("""
                INSERT INTO notifications (id, household_id, recipient_member_id, type, message, created_at, is_read)
                VALUES (@id, @h, @m, @t, @msg, @c, @r)
                """, tx)
                .P("id", n.Id).P("h", n.HouseholdId).P("m", n.RecipientMemberId).P("t", (short)n.Type)
                .P("msg", n.Message).P("c", n.CreatedAt).P("r", n.IsRead);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task MarkAllReadAsync(Guid memberId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("UPDATE notifications SET is_read = TRUE WHERE recipient_member_id = @m AND NOT is_read").P("m", memberId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class PgReportRepository(IDbConnectionFactory db) : IReportRepository
{
    public async Task<IReadOnlyList<CategorySpendSummary>> GetCategorySpendingAsync(Guid householdId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("""
            SELECT category,
                   SUM(amount) AS total_amount,
                   COUNT(*) AS expense_count,
                   CASE WHEN SUM(SUM(amount)) OVER () = 0 THEN 0
                        ELSE ROUND((SUM(amount) * 100.0 / SUM(SUM(amount)) OVER ())::numeric, 2)
                   END AS pct
              FROM expenses
             WHERE household_id = @h
               AND (@from::date IS NULL OR expense_date >= @from::date)
               AND (@to::date   IS NULL OR expense_date <= @to::date)
             GROUP BY category
             ORDER BY total_amount DESC
            """).P("h", householdId).P("from", from).P("to", to);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<CategorySpendSummary>();
        while (await r.ReadAsync(ct))
            list.Add(new CategorySpendSummary(
                (ExpenseCategory)r.GetInt16(0),
                r.GetDecimal(1),
                r.GetInt32(2),
                (double)r.GetDecimal(3)));
        return list;
    }

    public async Task<IReadOnlyList<MonthlySpendSummary>> GetMonthlySpendingAsync(Guid householdId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("""
            SELECT EXTRACT(YEAR FROM expense_date)::int AS yr,
                   EXTRACT(MONTH FROM expense_date)::int AS mo,
                   SUM(amount) AS total_amount,
                   COUNT(*) AS expense_count
              FROM expenses
             WHERE household_id = @h
               AND expense_date >= @from::date
               AND expense_date <= @to::date
             GROUP BY yr, mo
             ORDER BY yr, mo
            """).P("h", householdId).P("from", from).P("to", to);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<MonthlySpendSummary>();
        while (await r.ReadAsync(ct))
            list.Add(new MonthlySpendSummary(r.GetInt32(0), r.GetInt32(1), r.GetDecimal(2), r.GetInt32(3)));
        return list;
    }

    public async Task<IReadOnlyList<MemberSpendSummary>> GetMemberSpendingAsync(Guid householdId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var cmd = conn.Cmd("""
            WITH paid AS (
                SELECT paid_by_member_id AS member_id, COALESCE(SUM(amount), 0) AS total_paid
                  FROM expenses
                 WHERE household_id = @h
                   AND (@from::date IS NULL OR expense_date >= @from::date)
                   AND (@to::date   IS NULL OR expense_date <= @to::date)
                 GROUP BY paid_by_member_id
            ),
            shared AS (
                SELECT s.member_id, COALESCE(SUM(s.amount), 0) AS total_share
                  FROM expense_shares s
                  JOIN expenses e ON e.id = s.expense_id
                 WHERE e.household_id = @h
                   AND (@from::date IS NULL OR e.expense_date >= @from::date)
                   AND (@to::date   IS NULL OR e.expense_date <= @to::date)
                 GROUP BY s.member_id
            )
            SELECT m.id, m.display_name,
                   COALESCE(p.total_paid, 0) AS total_paid,
                   COALESCE(sh.total_share, 0) AS total_share
              FROM members m
              LEFT JOIN paid p ON p.member_id = m.id
              LEFT JOIN shared sh ON sh.member_id = m.id
             WHERE m.household_id = @h AND m.is_active = TRUE
             ORDER BY m.joined_at
            """).P("h", householdId).P("from", from).P("to", to);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var list = new List<MemberSpendSummary>();
        while (await r.ReadAsync(ct))
            list.Add(new MemberSpendSummary(r.GetGuid(0), r.GetString(1), r.GetDecimal(2), r.GetDecimal(3)));
        return list;
    }
}
