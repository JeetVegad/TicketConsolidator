# Database Logging Setup Guide

## Overview

TicketConsolidator supports optional centralized database logging to SQL Server. This feature allows you to:
- Store all application logs in a central database
- Query historical logs across multiple users
- Track application usage and errors centrally

## Prerequisites

- SQL Server (2012 or later)
- A database where you have permissions to create tables
- Network access to the SQL Server from the machine running TicketConsolidator

## Setup Instructions

### Step 1: Prepare Your Database

1. **Option A: Use Existing Database**
   - Identify the database name (e.g., `MyCompanyDB`)
   - Ensure you have `CREATE TABLE` permissions

2. **Option B: Create New Database**
   - Open SQL Server Management Studio (SSMS)
   - Right-click on "Databases" → "New Database"
   - Name it `TicketConsolidator`
   - Click OK

### Step 2: Run the SQL Setup Script

1. Locate the SQL script:
   - Installation folder: `C:\Program Files\Ticket Consolidator\SetUp\CreateDatabase.sql`
   - Or find it in the source code at `src\TicketConsolidator.UI\SetUp\CreateDatabase.sql`

2. Open the script in SQL Server Management Studio (SSMS)

3. **IMPORTANT**: If using an existing database, modify the script:
   - Find the line: `-- USE [TicketConsolidator]`
   - Uncomment it and change to your database name: `USE [YourDatabaseName]`

4. Execute the script (F5 or click Execute)

5. Verify success:
   ```sql
   SELECT * FROM AppLogs
   ```
   You should see an empty table with columns: LogID, LogDate, MachineName, UserName, LogLevel, Message, StackTrace

### Step 3: Configure Database Logging

You can enable database logging directly during installation.

#### Option A: During Installation (Recommended)

1. Run the TicketConsolidator installer (**v2.6** or later).
2. Check the box **"Enable Centralized Database Logging"**.
3. The installer automatically configures the following connection:
   - **Server:** `localhost`
   - **Database:** `TicketConsolidator`
   - **Authentication:** Windows Integrated (Trusted)

#### Option B: Custom Configuration (Remote/SQL Auth)

If you need to use a remote server or SQL Authentication (User/Password), follow these steps:

1. Install with the checkbox enabled.
2. Navigate to the installation directory (`C:\Program Files...`).
3. Open `appsettings.json` as Administrator.
4. Manually edit the `"LogDatabase"` connection string.

#### Option B: After Installation

1. Navigate to the installation directory:
   ```
   C:\Program Files\Ticket Consolidator\
   ```

2. Open `appsettings.json` in a text editor (run as Administrator)

3. Modify the following sections:
   ```json
   {
     "Logging": {
       "EnableDatabase": true
     },
     "ConnectionStrings": {
       "LogDatabase": "Server=YOUR_SERVER;Database=YOUR_DATABASE;Trusted_Connection=True;"
     }
   }
   ```

4. Save the file and restart the application

### Step 4: Verify Database Logging

1. Launch TicketConsolidator
2. Perform any action (e.g., scan for tickets)
3. Check the database:
   ```sql
   SELECT TOP 10 * FROM AppLogs ORDER BY LogDate DESC
   ```
4. You should see recent log entries

## Connection String Examples

### Windows Authentication (Recommended)
```
Server=localhost;Database=TicketConsolidator;Trusted_Connection=True;
```

### SQL Server Authentication
```
Server=localhost;Database=TicketConsolidator;User ID=appuser;Password=YourPassword123;
```

### Named Instance
```
Server=localhost\SQLEXPRESS;Database=TicketConsolidator;Trusted_Connection=True;
```

### Remote Server with Encryption
```
Server=sql-server-01.company.com;Database=TicketConsolidator;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;
```

## Troubleshooting

### Database Logging Not Working

1. **Check the connection string**
   - Open appsettings.json and verify the connection string
   - Test the connection using SQL Server Management Studio

2. **Check permissions**
   - Your Windows user or SQL user needs INSERT permissions on AppLogs table
   - Run this SQL to grant permissions:
     ```sql
     GRANT INSERT, SELECT ON AppLogs TO [DOMAIN\YourUser]
     ```

3. **Check the EnableDatabase flag**
   - Ensure `"EnableDatabase": true` in appsettings.json

4. **Review file logs**
   - File logs are always created even if database logging fails
   - Check: `Documents\TicketConsolidatorData\Logs\`
   - Look for `[DB-FAIL]` entries indicating database connection issues

### Network/Firewall Issues

If you see connection errors:
1. Verify SQL Server allows remote connections (SQL Server Configuration Manager)
2. Check Windows Firewall allows port 1433
3. Ping the SQL Server from your machine
4. Use `telnet YOUR_SERVER 1433` to test connectivity

## Disabling Database Logging

To disable database logging:

1. Edit `appsettings.json`
2. Change: `"EnableDatabase": false`
3. Restart the application

File-based logging will continue to work.

## Querying Logs

### View Recent Errors
```sql
SELECT TOP 50 
    LogDate,
    MachineName,
    UserName,
    Message
FROM AppLogs
WHERE LogLevel = 'Error'
ORDER BY LogDate DESC
```

### View Logs by User
```sql
SELECT 
    LogDate,
    LogLevel,
    Message
FROM AppLogs
WHERE UserName = 'DOMAIN\YourUser'
AND LogDate >= DATEADD(day, -7, GETDATE())
ORDER BY LogDate DESC
```

### Clean Old Logs
```sql
-- Delete logs older than 90 days
DELETE FROM AppLogs
WHERE LogDate < DATEADD(day, -90, GETDATE())
```

## Support

If you encounter issues:
1. Check file logs in `Documents\TicketConsolidatorData\Logs\`
2. Verify database setup by running the verification query
3. Test your connection string in SQL Server Management Studio
4. Contact your database administrator for permissions issues
