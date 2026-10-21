
CREATE TABLE IF NOT EXISTS households (
    id          UUID PRIMARY KEY,
    name        VARCHAR(100) NOT NULL,
    currency    VARCHAR(10)  NOT NULL DEFAULT 'UAH',
    created_at  TIMESTAMP    NOT NULL DEFAULT now()
);
ALTER TABLE households ADD COLUMN IF NOT EXISTS currency VARCHAR(10) NOT NULL DEFAULT 'UAH';
CREATE UNIQUE INDEX IF NOT EXISTS ux_households_name ON households(lower(name));

CREATE TABLE IF NOT EXISTS members (
    id                  UUID PRIMARY KEY,
    household_id        UUID         NOT NULL REFERENCES households(id) ON DELETE CASCADE,
    display_name        VARCHAR(100) NOT NULL,
    role                SMALLINT     NOT NULL DEFAULT 0,      -- 0 = Member, 1 = Admin
    avatar_color        CHAR(7)      NOT NULL DEFAULT '#F07B3F',
    joined_at           TIMESTAMP    NOT NULL DEFAULT now(),
    is_active           BOOLEAN      NOT NULL DEFAULT TRUE,
    is_email_confirmed  BOOLEAN      NOT NULL DEFAULT FALSE
);
ALTER TABLE members ADD COLUMN IF NOT EXISTS is_email_confirmed BOOLEAN NOT NULL DEFAULT FALSE;
CREATE INDEX IF NOT EXISTS ix_members_household ON members(household_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_members_household_active_name
    ON members(household_id, lower(display_name))
    WHERE is_active = TRUE;

CREATE TABLE IF NOT EXISTS user_accounts (
    member_id        UUID PRIMARY KEY REFERENCES members(id) ON DELETE CASCADE,
    email            VARCHAR(254) NOT NULL,
    password_hash    VARCHAR(128) NOT NULL,
    password_salt    VARCHAR(64)  NOT NULL,
    failed_attempts  INT          NOT NULL DEFAULT 0,
    locked_until     TIMESTAMP    NULL
);
DROP INDEX IF EXISTS ux_user_accounts_email;
CREATE INDEX IF NOT EXISTS ix_user_accounts_email ON user_accounts(lower(email));

CREATE TABLE IF NOT EXISTS expenses (
    id                   UUID PRIMARY KEY,
    household_id         UUID          NOT NULL REFERENCES households(id) ON DELETE CASCADE,
    title                VARCHAR(200)  NOT NULL,
    merchant             VARCHAR(200)  NULL,
    amount               NUMERIC(12,2) NOT NULL CHECK (amount > 0),
    expense_date         DATE          NOT NULL,
    category             SMALLINT      NOT NULL,          -- ExpenseCategory
    paid_by_member_id    UUID          NOT NULL REFERENCES members(id),
    split_method         SMALLINT      NOT NULL,          -- SplitMethod
    receipt_image_path   VARCHAR(500)  NULL,
    delete_approvals     TEXT          NOT NULL DEFAULT '',
    created_at           TIMESTAMP     NOT NULL DEFAULT now()
);
ALTER TABLE expenses ADD COLUMN IF NOT EXISTS delete_approvals TEXT NOT NULL DEFAULT '';
CREATE INDEX IF NOT EXISTS ix_expenses_household_date ON expenses(household_id, expense_date DESC);
CREATE INDEX IF NOT EXISTS ix_expenses_paid_by ON expenses(paid_by_member_id);

CREATE TABLE IF NOT EXISTS expense_shares (
    expense_id  UUID          NOT NULL REFERENCES expenses(id) ON DELETE CASCADE,
    member_id   UUID          NOT NULL REFERENCES members(id),
    amount      NUMERIC(12,2) NOT NULL CHECK (amount >= 0),
    percentage  NUMERIC(5,2)  NULL,
    PRIMARY KEY (expense_id, member_id)
);
CREATE INDEX IF NOT EXISTS ix_expense_shares_member ON expense_shares(member_id);

CREATE TABLE IF NOT EXISTS settlements (
    id              UUID PRIMARY KEY,
    household_id    UUID          NOT NULL REFERENCES households(id) ON DELETE CASCADE,
    from_member_id  UUID          NOT NULL REFERENCES members(id),
    to_member_id    UUID          NOT NULL REFERENCES members(id),
    amount          NUMERIC(12,2) NOT NULL CHECK (amount > 0),
    settled_at      TIMESTAMP     NOT NULL DEFAULT now(),
    CHECK (from_member_id <> to_member_id)
);
CREATE INDEX IF NOT EXISTS ix_settlements_household ON settlements(household_id, settled_at DESC);

CREATE TABLE IF NOT EXISTS chores (
    id            UUID PRIMARY KEY,
    household_id  UUID         NOT NULL REFERENCES households(id) ON DELETE CASCADE,
    title         VARCHAR(200) NOT NULL,
    assignee_id   UUID         NULL REFERENCES members(id),
    deadline      TIMESTAMP    NULL,
    status        SMALLINT     NOT NULL DEFAULT 0,      -- ChoreStatus
    rotation      SMALLINT     NOT NULL DEFAULT 2,      -- ChoreRotation
    created_at    TIMESTAMP    NOT NULL DEFAULT now(),
    completed_at  TIMESTAMP    NULL
);
CREATE INDEX IF NOT EXISTS ix_chores_household ON chores(household_id);
CREATE INDEX IF NOT EXISTS ix_chores_assignee_status ON chores(assignee_id, status);

CREATE TABLE IF NOT EXISTS notifications (
    id                   UUID PRIMARY KEY,
    household_id         UUID         NOT NULL REFERENCES households(id) ON DELETE CASCADE,
    recipient_member_id  UUID         NOT NULL REFERENCES members(id) ON DELETE CASCADE,
    type                 SMALLINT     NOT NULL,         -- NotificationType
    message              VARCHAR(500) NOT NULL,
    created_at           TIMESTAMP    NOT NULL DEFAULT now(),
    is_read              BOOLEAN      NOT NULL DEFAULT FALSE
);
CREATE INDEX IF NOT EXISTS ix_notifications_recipient ON notifications(recipient_member_id, created_at DESC);

CREATE OR REPLACE VIEW vw_monthly_category_expenses AS
SELECT
    e.household_id,
    date_trunc('month', e.expense_date)::date AS month_start,
    e.category,
    COUNT(e.id) AS expense_count,
    COALESCE(SUM(e.amount), 0)::numeric(12,2) AS total_amount
FROM expenses e
GROUP BY e.household_id, date_trunc('month', e.expense_date)::date, e.category;