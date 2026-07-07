-- atlas:import campaign.sql
-- atlas:import command_profile.sql
-- atlas:import player_profile.sql

create table public.career_record (
    id bigint generated always as identity primary key,
    uid uuid not null default gen_random_uuid(),
    player_profile_id bigint not null,
    command_profile_id bigint null,
    campaign_id bigint not null,
    theatre_id text not null,
    scenario_id text not null,
    doctrine text not null,
    side text not null,
    result text not null,
    turns_played integer not null,
    score integer not null default 0,
    units_lost integer not null default 0,
    battles_won integer not null default 0,
    battles_lost integer not null default 0,
    supply_breakdowns integer not null default 0,
    strengths text null,
    weaknesses text null,
    recommendation text null,
    summary_payload jsonb not null default '{}'::jsonb,
    completed_at timestamptz not null,
    created_at timestamptz not null default now(),
    created_by text not null,
    edited_at timestamptz not null default now(),
    edited_by text not null,
    version integer not null default 1,
    constraint uq_career_record_uid unique (uid),
    constraint uq_career_record_campaign unique (campaign_id),
    constraint fk_career_record_player_profile foreign key (player_profile_id) references public.player_profile (id),
    constraint fk_career_record_command_profile foreign key (command_profile_id) references public.command_profile (id),
    constraint fk_career_record_campaign foreign key (campaign_id) references public.campaign (id),
    constraint ck_career_record_doctrine check (doctrine in ('Balanced', 'MobileWarfare', 'DefensiveLogistics', 'AirSuperiority', 'Attrition')),
    constraint ck_career_record_side check (side in ('Axis', 'Allies')),
    constraint ck_career_record_result check (result in ('Victory', 'Defeat', 'Draw', 'Abandoned')),
    constraint ck_career_record_non_negative_counts check (
        turns_played >= 0
        and score >= 0
        and units_lost >= 0
        and battles_won >= 0
        and battles_lost >= 0
        and supply_breakdowns >= 0
    ),
    constraint ck_career_record_summary_payload_object check (jsonb_typeof(summary_payload) = 'object'),
    constraint ck_career_record_version_positive check (version > 0)
);

create index ix_career_record_player_profile_id on public.career_record (player_profile_id);
create index ix_career_record_command_profile_id on public.career_record (command_profile_id);
create index ix_career_record_result on public.career_record (result);
create index ix_career_record_completed_at on public.career_record (completed_at);
