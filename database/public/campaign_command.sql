-- atlas:import campaign.sql
-- atlas:import campaign_snapshot.sql
-- atlas:import campaign_turn.sql

create table public.campaign_command (
    id bigint generated always as identity primary key,
    uid uuid not null default gen_random_uuid(),
    campaign_id bigint not null,
    campaign_turn_id bigint not null,
    planned_from_snapshot_id bigint null,
    command_sequence integer not null,
    command_source text not null,
    side text not null,
    unit_id text null,
    region_id text null,
    command_type text not null,
    status text not null,
    command_payload jsonb not null default '{}'::jsonb,
    rejection_reason text null,
    created_at timestamptz not null default now(),
    created_by text not null,
    edited_at timestamptz not null default now(),
    edited_by text not null,
    version integer not null default 1,
    constraint uq_campaign_command_uid unique (uid),
    constraint uq_campaign_command_sequence unique (campaign_turn_id, command_source, command_sequence),
    constraint fk_campaign_command_campaign foreign key (campaign_id) references public.campaign (id),
    constraint fk_campaign_command_campaign_turn foreign key (campaign_turn_id) references public.campaign_turn (id),
    constraint fk_campaign_command_planned_from_snapshot foreign key (planned_from_snapshot_id) references public.campaign_snapshot (id),
    constraint ck_campaign_command_sequence_positive check (command_sequence > 0),
    constraint ck_campaign_command_source check (command_source in ('Human', 'AI', 'System')),
    constraint ck_campaign_command_side check (side in ('Axis', 'Allies')),
    constraint ck_campaign_command_type check (command_type in ('Move', 'Attack', 'Support', 'HoldPosition', 'Resupply', 'Recon')),
    constraint ck_campaign_command_status check (status in ('Planned', 'Accepted', 'Rejected', 'Resolved', 'Cancelled')),
    constraint ck_campaign_command_payload_object check (jsonb_typeof(command_payload) = 'object'),
    constraint ck_campaign_command_version_positive check (version > 0)
);

create index ix_campaign_command_campaign_id on public.campaign_command (campaign_id);
create index ix_campaign_command_campaign_turn_id on public.campaign_command (campaign_turn_id);
create index ix_campaign_command_planned_from_snapshot_id on public.campaign_command (planned_from_snapshot_id);
create index ix_campaign_command_source_status on public.campaign_command (command_source, status);

comment on table public.campaign_command is 'Stores human and AI turn orders. AI commands are persisted beside human commands so the resolved turn is auditable.';
