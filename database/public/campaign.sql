-- atlas:import command_profile.sql
-- atlas:import player_profile.sql
-- atlas:import user_account.sql

create table public.campaign (
    id bigint generated always as identity primary key,
    uid uuid not null default gen_random_uuid(),
    user_account_id bigint not null,
    player_profile_id bigint not null,
    command_profile_id bigint not null,
    theatre_id text not null,
    scenario_id text not null,
    name text not null,
    player_side text not null,
    enemy_side text not null,
    status text not null,
    current_turn_number integer not null default 0,
    max_turns integer not null,
    campaign_start_date date not null,
    current_campaign_date date not null,
    result text null,
    score integer null,
    started_at timestamptz not null default now(),
    completed_at timestamptz null,
    created_at timestamptz not null default now(),
    created_by text not null,
    edited_at timestamptz not null default now(),
    edited_by text not null,
    version integer not null default 1,
    constraint uq_campaign_uid unique (uid),
    constraint fk_campaign_user_account foreign key (user_account_id) references public.user_account (id),
    constraint fk_campaign_player_profile foreign key (player_profile_id) references public.player_profile (id),
    constraint fk_campaign_command_profile foreign key (command_profile_id) references public.command_profile (id),
    constraint ck_campaign_player_side check (player_side in ('Axis', 'Allies')),
    constraint ck_campaign_enemy_side check (enemy_side in ('Axis', 'Allies')),
    constraint ck_campaign_distinct_sides check (player_side <> enemy_side),
    constraint ck_campaign_status check (status in ('Draft', 'Active', 'Completed', 'Abandoned')),
    constraint ck_campaign_result check (result is null or result in ('Victory', 'Defeat', 'Draw', 'Abandoned')),
    constraint ck_campaign_turn_bounds check (current_turn_number >= 0 and max_turns > 0 and current_turn_number <= max_turns),
    constraint ck_campaign_version_positive check (version > 0)
);

create index ix_campaign_user_account_id on public.campaign (user_account_id);
create index ix_campaign_player_profile_id on public.campaign (player_profile_id);
create index ix_campaign_command_profile_id on public.campaign (command_profile_id);
create index ix_campaign_status on public.campaign (status);
create index ix_campaign_scenario on public.campaign (theatre_id, scenario_id);
