using DiscordAdminConsole.Configuration;
using DiscordAdminConsole.Logging;
using DiscordAdminConsole.Storage;

namespace DiscordAdminConsole.Discord;

public sealed class LeaderElection
{
    private readonly Func<AdminConsoleConfig> _config;
    private readonly object _roleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Action<bool>? _onRoleChanged;
    private Func<Task<bool>>? _claim;
    private Func<Task>? _release;
    private bool _isLeader;

    public LeaderElection(Func<AdminConsoleConfig> config)
    {
        _config = config;
    }

    public void Start(
        CancellationToken cancellationToken,
        Action<bool> onRoleChanged,
        Func<Task<bool>> claim,
        Func<Task> release)
    {
        lock (_roleLock)
        {
            if (_loop != null)
                return;

            _onRoleChanged = onRoleChanged;
            _claim = claim;
            _release = release;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loop = Task.Run(() => ElectionLoopAsync(_cts.Token));
        }
    }

    public async Task ReleaseLeadershipAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;
        Func<Task>? release;

        lock (_roleLock)
        {
            cts = _cts;
            loop = _loop;
            release = _release;
            cts?.Cancel();
        }

        if (loop != null)
        {
            try
            {
                await loop;
            }
            catch (Exception ex)
            {
                Log.Debug($"Leader election stopped with error: {ex.Message}");
            }
        }

        if (release != null)
        {
            try
            {
                await release();
            }
            catch (StorageUnavailableException ex)
            {
                Log.Debug($"Leadership release skipped: {ex.Message}");
            }
        }

        lock (_roleLock)
        {
            _cts?.Dispose();
            _cts = null;
            _loop = null;
            _claim = null;
            _release = null;
            _onRoleChanged = null;
            _isLeader = false;
        }
    }

    private async Task ElectionLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var leader = false;
                try
                {
                    leader = await _claim!();
                }
                catch (StorageUnavailableException ex)
                {
                    Log.Debug($"Leadership claim skipped: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"Leadership claim failed: {ex.Message}");
                }

                SetRole(leader);

                var seconds = Math.Max(1, _config().Discord.HeartbeatIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetRole(bool leader)
    {
        Action<bool>? callback = null;
        lock (_roleLock)
        {
            if (_isLeader == leader)
                return;

            _isLeader = leader;
            callback = _onRoleChanged;
        }

        try
        {
            callback?.Invoke(leader);
        }
        catch (Exception ex)
        {
            Log.Error($"Leadership role transition failed: {ex.Message}");
        }
    }
}
