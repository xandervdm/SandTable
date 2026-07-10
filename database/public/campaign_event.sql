-- atlas:import campaign.sql
-- atlas:import campaign_turn.sql

create table public.campaign_event (
    id bigint generated always as identity primary key,
    uid uuid not null default gen_random_uuid(),
    campaign_id bigint not null,
    campaign_turn_id bigint not null,
    event_sequence integer not null,
    event_type text not null,
    event_scope text not null,
    side text null,
    region_id text null,
    unit_id text null,
    summary text not null,
    event_payload jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    created_by text not null,
    edited_at timestamptz not null default now(),
    edited_by text not null,
    version integer not null default 1,
    constraint uq_campaign_event_uid unique (uid),
    constraint uq_campaign_event_sequence unique (campaign_turn_id, event_sequence),
    constraint fk_campaign_event_campaign foreign key (campaign_id) references public.campaign (id),
    constraint fk_campaign_event_campaign_turn foreign key (campaign_turn_id) references public.campaign_turn (id),
    constraint ck_campaign_event_sequence_positive check (event_sequence > 0),
    constraint ck_campaign_event_type check (event_type in ('Battle', 'Movement', 'Deployment', 'Supply', 'Recon', 'Tension', 'Victory', 'Scenario', 'System')),
    constraint ck_campaign_event_scope check (event_scope in ('Campaign', 'Turn', 'Region', 'Unit')),
    constraint ck_campaign_event_side check (side is null or side in ('Axis', 'Allies', 'Neutral')),
    constraint ck_campaign_event_payload_object check (jsonb_typeof(event_payload) = 'object'),
    constraint ck_campaign_event_version_positive check (version > 0)
);

create index ix_campaign_event_campaign_id on public.campaign_event (campaign_id);
create index ix_campaign_event_campaign_turn_id on public.campaign_event (campaign_turn_id);
create index ix_campaign_event_type on public.campaign_event (event_type);
create index ix_campaign_event_region_id on public.campaign_event (region_id) where region_id is not null;
create index ix_campaign_event_unit_id on public.campaign_event (unit_id) where unit_id is not null;
