using Dapper;
using Npgsql;

namespace SandTable.Api;

public sealed class DevPlayerBootstrapper
{
    private const string Actor = "system:dev-user";
    private const string Email = "dev@sandtable.local";

    public async Task<DevPlayerContext> EnsureAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var user = await connection.QuerySingleOrDefaultAsync<EntityIdentity>(
            new CommandDefinition(
                """
                select id, uid
                from public.user_account
                where lower(email) = lower(@Email)
                """,
                new { Email },
                transaction,
                cancellationToken: cancellationToken));

        user ??= await connection.QuerySingleAsync<EntityIdentity>(
            new CommandDefinition(
                """
                insert into public.user_account (
                    auth_provider,
                    auth_subject,
                    email,
                    display_name,
                    status,
                    is_development_user,
                    created_by,
                    edited_by
                )
                values (
                    'Development',
                    null,
                    @Email,
                    'Development Commander',
                    'Active',
                    true,
                    @Actor,
                    @Actor
                )
                returning id, uid
                """,
                new { Email, Actor },
                transaction,
                cancellationToken: cancellationToken));

        var playerProfile = await connection.QuerySingleOrDefaultAsync<EntityIdentity>(
            new CommandDefinition(
                """
                select id, uid
                from public.player_profile
                where user_account_id = @UserAccountId
                    and status = 'Active'
                """,
                new { UserAccountId = user.Id },
                transaction,
                cancellationToken: cancellationToken));

        playerProfile ??= await connection.QuerySingleAsync<EntityIdentity>(
            new CommandDefinition(
                """
                insert into public.player_profile (
                    user_account_id,
                    display_name,
                    status,
                    created_by,
                    edited_by
                )
                values (
                    @UserAccountId,
                    'Development Commander',
                    'Active',
                    @Actor,
                    @Actor
                )
                returning id, uid
                """,
                new { UserAccountId = user.Id, Actor },
                transaction,
                cancellationToken: cancellationToken));

        var commandProfile = await connection.QuerySingleOrDefaultAsync<EntityIdentity>(
            new CommandDefinition(
                """
                select id, uid
                from public.command_profile
                where player_profile_id = @PlayerProfileId
                    and is_default = true
                    and status = 'Active'
                """,
                new { PlayerProfileId = playerProfile.Id },
                transaction,
                cancellationToken: cancellationToken));

        commandProfile ??= await connection.QuerySingleAsync<EntityIdentity>(
            new CommandDefinition(
                """
                insert into public.command_profile (
                    player_profile_id,
                    display_name,
                    preferred_doctrine,
                    default_side,
                    animation_speed,
                    hints_enabled,
                    auto_save_enabled,
                    is_default,
                    status,
                    created_by,
                    edited_by
                )
                values (
                    @PlayerProfileId,
                    'Development Command Profile',
                    'Balanced',
                    'Axis',
                    'Normal',
                    true,
                    true,
                    true,
                    'Active',
                    @Actor,
                    @Actor
                )
                returning id, uid
                """,
                new { PlayerProfileId = playerProfile.Id, Actor },
                transaction,
                cancellationToken: cancellationToken));

        return new DevPlayerContext(user.Id, playerProfile.Id, commandProfile.Id, user.Uid, playerProfile.Uid, commandProfile.Uid);
    }
}

public sealed record DevPlayerContext(
    long UserAccountId,
    long PlayerProfileId,
    long CommandProfileId,
    Guid UserAccountUid,
    Guid PlayerProfileUid,
    Guid CommandProfileUid);

internal sealed class EntityIdentity
{
    public long Id { get; init; }
    public Guid Uid { get; init; }
}
