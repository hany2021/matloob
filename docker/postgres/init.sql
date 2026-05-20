-- Bootstrap script for the matloob Postgres container.
--
-- Runs ONCE, on first container init, when /var/lib/postgresql/data is empty.
-- Subsequent restarts skip this script. To rerun it, destroy the data volume:
--   docker compose -f docker/docker-compose.yml down -v
--
-- Scope: extensions only. DO NOT add business tables / sequences / functions
-- here — EF Core owns the schema starting in Phase 3 (migrations).

-- gen_random_uuid() comes with pgcrypto in Postgres 16.
-- (uuid-ossp is the older alternative; pgcrypto is preferred since PG13.)
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- Case-insensitive text — useful for email columns + lookups that must not
-- treat 'a@b.com' and 'A@b.com' as different rows. EF Core can map citext
-- to System.String with a value converter when needed.
CREATE EXTENSION IF NOT EXISTS citext;
