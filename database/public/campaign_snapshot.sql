-- atlas:import campaign.sql
-- atlas:import campaign_turn.sql

create table public.campaign_snapshot (
    id bigint generated always as identity primary key,
    uid uuid not null default gen_random_uuid(),
    campaign_id bigint not null,
    campaign_turn_id bigint null,
    snapshot_type text not null,
    turn_number integer not null,
    game_state jsonb not null,
    engine_version text not null,
    random_seed integer null,
    state_hash text null,
    is_latest boolean not null default false,
    created_at timestamptz not null default now(),
    created_by text not null,
    edited_at timestamptz not null default now(),
    edited_by text not null,
    version integer not null default 1,
    constraint uq_campaign_snapshot_uid unique (uid),
    constraint fk_campaign_snapshot_campaign foreign key (campaign_id) references public.campaign (id),
    constraint fk_campaign_snapshot_campaign_turn foreign key (campaign_turn_id) references public.campaign_turn (id),
    constraint ck_campaign_snapshot_type check (snapshot_type in ('Initial', 'TurnStart', 'TurnResolved', 'Autosave', 'ManualSave')),
    constraint ck_campaign_snapshot_turn_number_non_negative check (turn_number >= 0),
    constraint ck_campaign_snapshot_game_state_object check (jsonb_typeof(game_state) = 'object'),
    constraint ck_campaign_snapshot_version_positive check (version > 0)
);

create index ix_campaign_snapshot_campaign_id on public.campaign_snapshot (campaign_id);
create index ix_campaign_snapshot_campaign_turn_id on public.campaign_snapshot (campaign_turn_id);
create index ix_campaign_snapshot_type on public.campaign_snapshot (snapshot_type);
create unique index ux_campaign_snapshot_latest_per_campaign on public.campaign_snapshot (campaign_id) where is_latest = true;

comment on table public.campaign_snapshot is 'Serialized engine state persisted after campaign creation, saves, autosaves, and resolved turns. API remains stateless between requests.';
