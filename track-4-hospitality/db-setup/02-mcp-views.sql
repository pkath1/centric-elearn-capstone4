-- 02-mcp-views.sql
-- Read-only views for the Larchmont MCP server (mcp/Larchmont.Mcp).
--
-- Run order: V001 to V005 (migrations, applied by the API), then this file, then 03-create-mcp-user.sql.
--
-- MUST BE RUN WITH SUBSTITUTION. Do not run this file as it is: the placeholder __MCP_HOTEL_ID__ is not valid SQL.
-- Replace it with the integer hotels.id of the one hotel the MCP server may read, by piping through sed:
--   sed "s/__MCP_HOTEL_ID__/1/g" db-setup/02-mcp-views.sql | mysql -h 127.0.0.1 -P 3306 -u ADMIN -p larchmont
--   sed "s/__MCP_HOTEL_ID__/1/g" db-setup/02-mcp-views.sql | mysql -h 127.0.0.1 -P 3306 -u ADMIN -p larchmont_scratch
-- Use your hotel's id in place of 1. Enter the password at the prompt, never on the command line.
-- This file has no USE statement and no database name, so the same file works for both databases.
-- Run it as an administrator. The view definer is the account that runs this file.
--
-- Scope (one hotel per install), fixed in the view definitions:
--   Every view filters on WHERE hotel_id = <the substituted id>. MySQL refuses views that read user
--   variables (ERROR 1351), so the hotel is part of each definition. To change hotel, re-run with another id.
--   The MCP server's LARCHMONT_MCP_HOTEL_ID must equal that id. The server only writes it to the audit log.
--   The views take no parameters.
--
-- Personal data: no view exposes guest names, emails, phones, addresses, staff emails or display names,
--   password hashes, last login times, or any free-text notes (rooms.notes, booking_notes, room_status_history.reason,
--   rooms.ooo_reason). Out-of-order rooms show only whether a reason exists.
--
-- Security definer: views run with the definer's privileges, so the MCP user needs no base-table grants.
-- Times are UTC (the spec's convention): UTC_TIMESTAMP() and UTC_DATE() throughout, no server time zone.
--
-- The audit table mcp_query_audit is created in 03-create-mcp-user.sql, not here.

-- Rooms with SLA status and minutes open. Includes open and closed turnarounds; the MCP tool filters on is_open.
CREATE OR REPLACE SQL SECURITY DEFINER VIEW v_mcp_room_turnarounds AS
SELECT r.hotel_id,
       rt.room_id,
       r.room_number,
       r.floor,
       t.code AS room_type_code,
       r.status AS room_status,
       rt.is_open,
       rt.checkout_at,
       rt.ready_at,
       rt.target_minutes,
       TIMESTAMPDIFF(MINUTE, rt.checkout_at, COALESCE(rt.ready_at, UTC_TIMESTAMP())) AS minutes_open,
       CASE
         WHEN TIMESTAMPDIFF(MINUTE, rt.checkout_at, COALESCE(rt.ready_at, UTC_TIMESTAMP())) > rt.target_minutes
           THEN 'Breach'
         ELSE 'Within'
       END AS sla_status
FROM room_turnarounds rt
JOIN rooms r ON r.id = rt.room_id
JOIN room_types t ON t.id = r.room_type_id
WHERE r.hotel_id = __MCP_HOTEL_ID__;

-- Every room with its status (board and occupancy inputs).
CREATE OR REPLACE SQL SECURITY DEFINER VIEW v_mcp_rooms AS
SELECT r.hotel_id,
       r.id AS room_id,
       r.room_number,
       r.floor,
       t.code AS room_type_code,
       r.status AS room_status,
       r.simple_turn,
       r.ooo_since,
       r.ooo_expected_return
FROM rooms r
JOIN room_types t ON t.id = r.room_type_id
WHERE r.hotel_id = __MCP_HOTEL_ID__;

-- Rooms currently OutOfOrder. The reason text is masked: only has_reason is shown.
CREATE OR REPLACE SQL SECURITY DEFINER VIEW v_mcp_out_of_order_rooms AS
SELECT r.hotel_id,
       r.id AS room_id,
       r.room_number,
       r.floor,
       t.code AS room_type_code,
       r.ooo_since,
       r.ooo_expected_return,
       CASE WHEN r.ooo_reason IS NULL OR r.ooo_reason = '' THEN 0 ELSE 1 END AS has_reason
FROM rooms r
JOIN room_types t ON t.id = r.room_type_id
WHERE r.hotel_id = __MCP_HOTEL_ID__
  AND r.status = 'OutOfOrder';

-- Rooms per housekeeper per floor, with counts by status. Only the user id is shown, never a name or contact.
-- A NULL housekeeper_user_id means the floor has no active assignment.
CREATE OR REPLACE SQL SECURITY DEFINER VIEW v_mcp_housekeeper_load AS
SELECT r.hotel_id,
       r.floor,
       fa.housekeeper_user_id,
       COUNT(*) AS rooms_total,
       CAST(SUM(r.status = 'Dirty') AS SIGNED) AS rooms_dirty,
       CAST(SUM(r.status = 'Cleaning') AS SIGNED) AS rooms_cleaning,
       CAST(SUM(r.status = 'Inspected') AS SIGNED) AS rooms_inspected,
       CAST(SUM(r.status = 'Ready') AS SIGNED) AS rooms_ready,
       CAST(SUM(r.status = 'OutOfOrder') AS SIGNED) AS rooms_out_of_order
FROM rooms r
LEFT JOIN floor_assignments fa
       ON fa.hotel_id = r.hotel_id
      AND fa.floor = r.floor
      AND fa.active = 1
WHERE r.hotel_id = __MCP_HOTEL_ID__
GROUP BY r.hotel_id, r.floor, fa.housekeeper_user_id;

-- Confirmed bookings arriving today, with the assigned room's status. Booking reference only, no guest.
-- room_id and room_status are NULL when no room is assigned yet.
CREATE OR REPLACE SQL SECURITY DEFINER VIEW v_mcp_arrivals_today AS
SELECT b.hotel_id,
       b.reference AS booking_reference,
       b.status AS booking_status,
       b.check_in,
       b.check_out,
       b.room_id,
       r.room_number,
       r.status AS room_status
FROM bookings b
LEFT JOIN rooms r ON r.id = b.room_id
WHERE b.hotel_id = __MCP_HOTEL_ID__
  AND b.check_in = UTC_DATE()
  AND b.status = 'Confirmed';

-- Room inventory per room type: total, out of order, and available (out-of-order rooms are not available, BR-20).
CREATE OR REPLACE SQL SECURITY DEFINER VIEW v_mcp_room_inventory AS
SELECT r.hotel_id,
       t.code AS room_type_code,
       COUNT(*) AS rooms_total,
       CAST(SUM(r.status = 'OutOfOrder') AS SIGNED) AS rooms_out_of_order,
       CAST(SUM(r.status <> 'OutOfOrder') AS SIGNED) AS rooms_available
FROM rooms r
JOIN room_types t ON t.id = r.room_type_id
WHERE r.hotel_id = __MCP_HOTEL_ID__
GROUP BY r.hotel_id, t.code;

-- Bookings as stays (dates, status, room). No guest columns and no notes.
CREATE OR REPLACE SQL SECURITY DEFINER VIEW v_mcp_stays AS
SELECT b.hotel_id,
       b.reference AS booking_reference,
       b.status AS booking_status,
       b.check_in,
       b.check_out,
       b.room_id,
       r.room_number,
       r.status AS room_status,
       t.code AS room_type_code
FROM bookings b
LEFT JOIN rooms r ON r.id = b.room_id
LEFT JOIN room_types t ON t.id = b.room_type_id
WHERE b.hotel_id = __MCP_HOTEL_ID__;
