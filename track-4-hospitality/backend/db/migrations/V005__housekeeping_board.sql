-- Housekeeping board schema. Roles need no change: users.role is a plain VARCHAR(20) with no CHECK or enum.

ALTER TABLE rooms MODIFY status VARCHAR(20) NOT NULL DEFAULT 'Ready';
UPDATE rooms SET status = 'Ready' WHERE status = 'Clean';
ALTER TABLE rooms ADD CONSTRAINT ck_rooms_status CHECK (status IN ('Dirty', 'Cleaning', 'Inspected', 'Ready', 'OutOfOrder'));
ALTER TABLE rooms
  ADD COLUMN ooo_reason VARCHAR(255) NULL,
  ADD COLUMN ooo_since DATETIME NULL,
  ADD COLUMN ooo_expected_return DATETIME NULL,
  ADD COLUMN simple_turn TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN row_version INT NOT NULL DEFAULT 1;

ALTER TABLE room_types ADD COLUMN sla_minutes INT NOT NULL DEFAULT 45;
UPDATE room_types SET sla_minutes = 75 WHERE code IN ('STE', 'FAM');

ALTER TABLE room_status_history ADD COLUMN reason VARCHAR(255) NULL;

CREATE TABLE room_turnarounds (
  id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  room_id INT NOT NULL,
  booking_id INT NULL,
  checkout_at DATETIME NOT NULL,
  ready_at DATETIME NULL,
  target_minutes INT NOT NULL,
  is_open TINYINT(1) NOT NULL DEFAULT 1,
  open_room_id INT GENERATED ALWAYS AS (CASE WHEN is_open = 1 THEN room_id ELSE NULL END) STORED,
  UNIQUE KEY ux_room_turnarounds_open_room (open_room_id),
  KEY ix_room_turnarounds_room (room_id, checkout_at),
  CONSTRAINT fk_room_turnarounds_room FOREIGN KEY (room_id) REFERENCES rooms (id),
  CONSTRAINT fk_room_turnarounds_booking FOREIGN KEY (booking_id) REFERENCES bookings (id)
);

CREATE TABLE floor_assignments (
  id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  hotel_id INT NOT NULL,
  floor INT NOT NULL,
  housekeeper_user_id INT NOT NULL,
  active TINYINT(1) NOT NULL DEFAULT 1,
  active_floor INT GENERATED ALWAYS AS (CASE WHEN active = 1 THEN floor ELSE NULL END) STORED,
  UNIQUE KEY ux_floor_assignments_active (hotel_id, active_floor),
  KEY ix_floor_assignments_user (housekeeper_user_id),
  CONSTRAINT fk_floor_assignments_hotel FOREIGN KEY (hotel_id) REFERENCES hotels (id),
  CONSTRAINT fk_floor_assignments_user FOREIGN KEY (housekeeper_user_id) REFERENCES users (id)
);

CREATE TABLE room_spot_checks (
  id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  room_id INT NOT NULL,
  status_history_id BIGINT NOT NULL,
  created_at DATETIME NOT NULL,
  checked_at DATETIME NULL,
  checked_by_user_id INT NULL,
  UNIQUE KEY ux_room_spot_checks_history (status_history_id),
  KEY ix_room_spot_checks_room (room_id, checked_at),
  CONSTRAINT fk_room_spot_checks_room FOREIGN KEY (room_id) REFERENCES rooms (id),
  CONSTRAINT fk_room_spot_checks_history FOREIGN KEY (status_history_id) REFERENCES room_status_history (id),
  CONSTRAINT fk_room_spot_checks_user FOREIGN KEY (checked_by_user_id) REFERENCES users (id)
);
