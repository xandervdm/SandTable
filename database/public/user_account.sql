create table public.user_account (
    id bigint generated always as identity primary key,
    uid uuid not null default gen_random_uuid(),
    auth_provider text not null,
    auth_subject text null,
    email text not null,
    display_name text not null,
    status text not null,
    is_development_user boolean not null default false,
    last_signed_in_at timestamptz null,
    created_at timestamptz not null default now(),
    created_by text not null,
    edited_at timestamptz not null default now(),
    edited_by text not null,
    version integer not null default 1,
    constraint uq_user_account_uid unique (uid),
    constraint uq_user_account_auth_subject unique (auth_provider, auth_subject),
    constraint ck_user_account_auth_provider check (auth_provider in ('Development', 'OpenIddict')),
    constraint ck_user_account_status check (status in ('Pending', 'Active', 'Disabled', 'Removed')),
    constraint ck_user_account_version_positive check (version > 0),
    constraint ck_user_account_staged_auth check (
        (auth_provider = 'Development' and is_development_user = true and auth_subject is null)
        or (auth_provider = 'OpenIddict' and is_development_user = false and auth_subject is not null)
    )
);

create unique index ux_user_account_email_lower on public.user_account (lower(email));
create index ix_user_account_status on public.user_account (status);

comment on table public.user_account is 'Application owner account. Development rows are allowed only for early V1; OpenIddict-backed ownership is required before staging.';
comment on column public.user_account.auth_subject is 'OpenIddict subject once real authentication is enabled; null only for the implicit development user path.';
