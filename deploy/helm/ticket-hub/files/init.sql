-- One database and one login per service. Nothing here grants cross-database access.
-- Run by the db-init container with sqlcmd variables for each password.
SET NOCOUNT ON;

DECLARE @services TABLE (db sysname, login sysname, pwd nvarchar(128));
INSERT INTO @services VALUES
    (N'TicketHub_Identity',  N'identity_svc',  N'$(IDENTITY_DB_PASSWORD)'),
    (N'TicketHub_Events',    N'events_svc',    N'$(EVENTS_DB_PASSWORD)'),
    (N'TicketHub_Seating',   N'seating_svc',   N'$(SEATING_DB_PASSWORD)'),
    (N'TicketHub_Orders',    N'orders_svc',    N'$(ORDERS_DB_PASSWORD)'),
    (N'TicketHub_Notifier',  N'notifier_svc',  N'$(NOTIFIER_DB_PASSWORD)'),
    (N'TicketHub_Pricing',   N'pricing_svc',   N'$(PRICING_DB_PASSWORD)'),
    (N'TicketHub_Analytics', N'analytics_svc', N'$(ANALYTICS_DB_PASSWORD)');

DECLARE @db sysname, @login sysname, @pwd nvarchar(128), @sql nvarchar(max);
DECLARE svc CURSOR LOCAL FAST_FORWARD FOR SELECT db, login, pwd FROM @services;
OPEN svc;
FETCH NEXT FROM svc INTO @db, @login, @pwd;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF DB_ID(@db) IS NULL
    BEGIN
        SET @sql = N'CREATE DATABASE ' + QUOTENAME(@db) + N';';
        EXEC (@sql);
        SET @sql = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET READ_COMMITTED_SNAPSHOT ON;';
        EXEC (@sql);
    END

    IF SUSER_ID(@login) IS NULL
        SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@pwd, '''') + N', DEFAULT_DATABASE = ' + QUOTENAME(@db) + N', CHECK_POLICY = OFF;';
    ELSE
        SET @sql = N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@pwd, '''') + N';';
    EXEC (@sql);

    -- each service owns its schema and runs its own migrations, so db_owner on its own database only
    SET @sql = N'USE ' + QUOTENAME(@db) + N';
        IF USER_ID(' + QUOTENAME(@login, '''') + N') IS NULL CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N';
        ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@login) + N';';
    EXEC (@sql);

    PRINT N'ready: ' + @db;
    FETCH NEXT FROM svc INTO @db, @login, @pwd;
END
CLOSE svc;
DEALLOCATE svc;
