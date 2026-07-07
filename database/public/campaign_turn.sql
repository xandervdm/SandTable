-- atlas:import campaign.sql

create table public.campaign_turn (
    id bigint generated always as identity primary key,
    uid uuid not null default gen_random_uuid(),
    campaign_id bigint not null,
    turn_number integer not null,
    status text not null,
    resolution_mode text not null default 'Simultaneous',
    random_seed integer not null,
    player_commands_committed_at timestamptz null,
    ai_commands_planned_at timestamptz null,
    resolved_at timestamptz null,
    turn_summary text null,
    created_at timestamptz not null default now(),
    created_by text not null,
    edited_at timestamptz not null default now(),
    edited_by text not null,
    version integer not null default 1,
    constraint uq_campaign_turn_uid unique (uid),
    constraint uq_campaign_turn_campaign_turn_number unique (campaign_id, turn_number),
    constraint fk_campaign_turn_campaign foreign key (campaign_id) references public.campaign (id),
    constraint ck_campaign_turn_status check (status in ('Planning', 'Committed', 'Resolving', 'Resolved', 'Cancelled')),
    constraint ck_campaign_turn_resolution_mode check (resolution_mode = 'Simultaneous'),
    constraint ck_campaign_turn_turn_number_positive check (turn_number > 0),
    constraint ck_campaign_turn_version_positive check (version > 0)
);

create index ix_campaign_turn_campaign_id on public.campaign_turn (campaign_id);
create index ix_campaign_turn_status on public.campaign_turn (status);

comment on table public.campaign_turn is 'One simultaneous-resolution turn. Human and AI commands are planned from the same starting snapshot and resolved together.';
