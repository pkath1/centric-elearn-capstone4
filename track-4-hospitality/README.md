# Larchmont Hotels

Larchmont Hotels runs six independent-feeling hotels in Edinburgh, Manchester, Leeds, London Bridge, Bath and Brighton. The reservations team keeps one small internal system for hotels, room types, rooms, guests, bookings, rates and staff, with an occupancy report for the Rooms Director and an importer for the channel manager's booking export. It was built quickly by a contractor who has since moved on, and the Rooms Director now has requests waiting for it.

## Run it

Needs: .NET 8 SDK, Node 20, and a MySQL 8 server on port 3306 that you can reach as root.

```
mysql -h 127.0.0.1 -P 3306 -u root -p < db-setup/01-create-users.sql

cd backend
dotnet run --project Larchmont.Api

cd frontend
npm install
npm run dev
```

API: http://localhost:5080/swagger (Development only) - UI: http://localhost:5173 - Tests: `cd backend && dotnet test`

MySQL on another host or port: set the environment variable `ConnectionStrings__Larchmont` to a full connection string (server, port, database, user, password) before starting the API. The existing tests use an in-memory database and do not need MySQL.

`docker compose up` reads `MYSQL_ROOT_PASSWORD` from your shell (or a gitignored `.env` next to `compose.yaml`) and refuses to start without it. No password is stored in the repository.
