-- atlas:import user_account.sql

create table public.player_profile (
    id bigint generated always as identity primary key,
    uid uuid not null default gen_random_uuid(),
    user_account_id bigint not null,
    display_name text not null,
    status text not null,
    created_at timestamptz not null default now(),
    created_by text not null,
    edited_at timestamptz not null default now(),
    edited_by text not null,
    version integer not null default 1,
    constraint uq_player_profile_uid unique (uid),
    constraint uq_player_profile_user_account unique (user_account_id),
    constraint fk_player_profile_user_account foreign key (user_account_id) references public.user_account (id),
    constraint ck_player_profile_status check (status in ('Active', 'Disabled')),
    constraint ck_player_profile_version_positive check (version > 0)
);

create index ix_player_profile_user_account_id on public.player_profile (user_account_id);
create index ix_player_profile_status on public.player_profile (status);
