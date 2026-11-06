-- VIRTUAL LOFT — Supabase schema (prototype).
-- Run once in the Supabase SQL editor. Uses gen_random_uuid() (pgcrypto,
-- enabled by default on Supabase). RLS policies below are PERMISSIVE for the
-- prototype backend (anon key); tighten before any real user data.

create extension if not exists "pgcrypto";

-- Teams owning lofts.
create table if not exists teams (
  id uuid primary key default gen_random_uuid(),
  name text not null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

-- One row per virtual loft room. Full wall/agent layout lives in `layout`
-- (jsonb: { boards, agents }); `version` + `updated_at` make cloud JSON the
-- source of truth for the travel test (kos -> cafe reload).
create table if not exists lofts (
  id uuid primary key default gen_random_uuid(),
  team_id uuid references teams (id) on delete cascade,
  room_id text not null unique,
  layout jsonb not null default '{"boards": [], "agents": []}',
  version integer not null default 1,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index if not exists lofts_team_idx on lofts (team_id);

-- Wall boards (Notes / Kanban / Calendar / Log). board_id is the wall-board
-- id from the loft JSON (e.g. "kanban-main"); unique per loft.
create table if not exists boards (
  id uuid primary key default gen_random_uuid(),
  loft_id uuid not null references lofts (id) on delete cascade,
  board_id text not null,
  type text not null,
  wall text not null default 'main',
  pos jsonb not null default '{}',
  size jsonb not null default '{}',
  content jsonb not null default '{}',
  version integer not null default 1,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (loft_id, board_id)
);
create index if not exists boards_loft_idx on boards (loft_id);

-- Tasks / cards (mirrors checkboard cards for querying + assignment).
create table if not exists tasks (
  id uuid primary key default gen_random_uuid(),
  team_id uuid references teams (id) on delete cascade,
  board_id uuid references boards (id) on delete set null,
  title text not null,
  status text not null default 'ToDo',
  assignee text not null default '',
  version integer not null default 1,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index if not exists tasks_team_idx on tasks (team_id);
create index if not exists tasks_board_idx on tasks (board_id);

-- Agent run log: one row per runAgent() execution.
create table if not exists agent_runs (
  id uuid primary key default gen_random_uuid(),
  task_id uuid references tasks (id) on delete set null,
  agent_role text not null,
  input jsonb not null default '{}',
  output jsonb not null default '{}',
  status text not null default 'done',
  version integer not null default 1,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index if not exists agent_runs_task_idx on agent_runs (task_id);

-- Row-level security (prototype-permissive; tighten for production).
alter table teams enable row level security;
alter table lofts enable row level security;
alter table boards enable row level security;
alter table tasks enable row level security;
alter table agent_runs enable row level security;

create policy "prototype open access" on teams for all using (true) with check (true);
create policy "prototype open access" on lofts for all using (true) with check (true);
create policy "prototype open access" on boards for all using (true) with check (true);
create policy "prototype open access" on tasks for all using (true) with check (true);
create policy "prototype open access" on agent_runs for all using (true) with check (true);
