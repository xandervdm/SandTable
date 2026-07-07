-- atlas:import player_profile.sql

create table public.command_profile (
    id bigint generated always as identity primary key,
    uid uuid not null default gen_random_uuid(),
    player_profile_id bigint not null,
    display_name text not null,
    preferred_doctrine text not null,
    default_side text not null,
    animation_speed text not null,
    hints_enabled boolean not null default true,
    auto_save_enabled boolean not null default true,
    is_default boolean not null default true,
    status text not null,
    created_at timestamptz not null default now(),
    created_by text not null,
    edited_at timestamptz not null default now(),
    edited_by text not null,
    version integer not null default 1,
    constraint uq_command_profile_uid unique (uid),
    constraint fk_command_profile_player_profile foreign key (player_profile_id) references public.player_profile (id),
    constraint ck_command_profile_preferred_doctrine check (preferred_doctrine in ('Balanced', 'MobileWarfare', 'DefensiveLogistics', 'AirSuperiority', 'Attrition')),
    constraint ck_command_profile_default_side check (default_side in ('Axis', 'Allies')),
    constraint ck_command_profile_animation_speed check (animation_speed in ('Slow', 'Normal', 'Fast', 'Instant')),
    constraint ck_command_profile_status check (status in ('Active', 'Archived')),
    constraint ck_command_profile_version_positive check (version > 0)
);

create index ix_command_profile_player_profile_id on public.command_profile (player_profile_id);
create index ix_command_profile_status on public.command_profile (status);
create unique index ux_command_profile_default_per_player on public.command_profile (player_profile_id) where is_default = true and status = 'Active';
