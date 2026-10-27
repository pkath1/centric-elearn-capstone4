-- 03-create-mcp-user.sql
-- Creates the MySQL user for the Larchmont MCP server, its grants, and the audit table.
-- Run AFTER 02-mcp-views.sql. Run as an administrator. The MCP server never runs this file.
--
-- Two placeholders are substituted at run time. Neither holds a real value in this file:
--   __MCP_PASSWORD__   the password for larchmont_mcp
--   __MCP_DATABASE__   the database that holds the views, for example larchmont (or larchmont_scratch)
--
-- How to run (Git Bash). The password is read silently and is never written to disk:
--   read -r -s MCP_PW; echo
--   sed -e "s/__MCP_PASSWORD__/$MCP_PW/" -e "s/__MCP_DATABASE__/larchmont/" db-setup/03-create-mcp-user.sql \
--     | mysql -h HOST -u ADMIN -p larchmont
--   unset MCP_PW
-- Rules for the password: no single quote, no slash, no ampersand, no backslash (sed and SQL quoting).
-- Then put the same password into the connection string in LARCHMONT_MCP_CONNECTION (an environment variable,
-- never a file). See docs/mcp-security.md.
--
-- Grants: SELECT on the seven v_mcp_* views only, and INSERT on mcp_query_audit only.
-- No grants on base tables, users, or credentials. The user has no UPDATE or DELETE on the audit table.
-- New accounts start with USAGE only, so nothing else is granted here.

-- Audit table: one row per MCP call, including rejected statements. Created here so the grant below can point at it.
CREATE TABLE IF NOT EXISTS mcp_query_audit (
  id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  created_at DATETIME(3) NOT NULL,
  tool VARCHAR(40) NOT NULL,
  db_user VARCHAR(160) NULL,
  hotel_id INT NOT NULL,
  statement_text TEXT NULL,
  row_count INT NULL,
  outcome VARCHAR(20) NOT NULL,
  detail VARCHAR(255) NULL,
  KEY ix_mcp_query_audit_created (created_at)
);

-- The account. Limits: 1000 queries per hour and 4 concurrent connections.
CREATE USER IF NOT EXISTS 'larchmont_mcp'@'%' IDENTIFIED BY '__MCP_PASSWORD__';
ALTER USER 'larchmont_mcp'@'%' IDENTIFIED BY '__MCP_PASSWORD__'
  WITH MAX_QUERIES_PER_HOUR 1000 MAX_USER_CONNECTIONS 4;

-- Views only.
GRANT SELECT ON `__MCP_DATABASE__`.v_mcp_room_turnarounds TO 'larchmont_mcp'@'%';
GRANT SELECT ON `__MCP_DATABASE__`.v_mcp_rooms TO 'larchmont_mcp'@'%';
GRANT SELECT ON `__MCP_DATABASE__`.v_mcp_out_of_order_rooms TO 'larchmont_mcp'@'%';
GRANT SELECT ON `__MCP_DATABASE__`.v_mcp_housekeeper_load TO 'larchmont_mcp'@'%';
GRANT SELECT ON `__MCP_DATABASE__`.v_mcp_arrivals_today TO 'larchmont_mcp'@'%';
GRANT SELECT ON `__MCP_DATABASE__`.v_mcp_room_inventory TO 'larchmont_mcp'@'%';
GRANT SELECT ON `__MCP_DATABASE__`.v_mcp_stays TO 'larchmont_mcp'@'%';

-- Audit: insert only.
GRANT INSERT ON `__MCP_DATABASE__`.mcp_query_audit TO 'larchmont_mcp'@'%';

-- Check after running (expected: USAGE, the seven SELECTs, and INSERT on mcp_query_audit):
--   SHOW GRANTS FOR 'larchmont_mcp'@'%';
