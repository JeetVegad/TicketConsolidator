-- ============================================
-- TicketConsolidator Database Logging Setup
-- ============================================
-- This script creates the required table for database logging
-- Run this script on your SQL Server database before enabling database logging

USE [master]
GO

-- Optional: Create a dedicated database (or use existing database)
-- Uncomment the lines below if you want to create a new database
/*
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'TicketConsolidator')
BEGIN
    CREATE DATABASE [TicketConsolidator]
END
GO

USE [TicketConsolidator]
GO
*/

-- Create AppLogs Table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AppLogs]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AppLogs](
        [LogID] [int] IDENTITY(1,1) NOT NULL,
        [LogDate] [datetime] NOT NULL DEFAULT (GETDATE()),
        [MachineName] [nvarchar](100) NULL,
        [UserName] [nvarchar](100) NULL,
        [LogLevel] [nvarchar](20) NULL,
        [Message] [nvarchar](max) NULL,
        [StackTrace] [nvarchar](max) NULL,
        CONSTRAINT [PK_AppLogs] PRIMARY KEY CLUSTERED ([LogID] ASC)
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
    
    PRINT 'Table AppLogs created successfully'
END
ELSE
BEGIN
    PRINT 'Table AppLogs already exists'
END
GO

-- Create indexes for better query performance
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = N'IX_AppLogs_LogDate' AND object_id = OBJECT_ID(N'[dbo].[AppLogs]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AppLogs_LogDate] ON [dbo].[AppLogs]
    (
        [LogDate] DESC
    )
    PRINT 'Index IX_AppLogs_LogDate created successfully'
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = N'IX_AppLogs_UserName' AND object_id = OBJECT_ID(N'[dbo].[AppLogs]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_AppLogs_UserName] ON [dbo].[AppLogs]
    (
        [UserName] ASC
    )
    INCLUDE ([LogDate], [LogLevel], [Message])
    PRINT 'Index IX_AppLogs_UserName created successfully'
END
GO

-- Verify table creation
SELECT 
    'AppLogs' AS TableName,
    COUNT(*) AS RecordCount,
    MAX(LogDate) AS LatestLog
FROM [dbo].[AppLogs]
GO

PRINT ''
PRINT '============================================'
PRINT 'Database setup completed successfully!'
PRINT '============================================'
PRINT ''
PRINT 'Next Steps:'
PRINT '1. Note your database name and server name'
PRINT '2. Update the connection string in appsettings.json during installation'
PRINT '3. Enable database logging when prompted during installation'
PRINT ''
GO
