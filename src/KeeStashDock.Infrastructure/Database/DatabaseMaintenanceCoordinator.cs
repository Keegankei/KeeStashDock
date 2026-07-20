namespace KeeStashDock.Infrastructure.Database;

/// <summary>
/// Serializes database connection lifetimes and provides an exclusive
/// maintenance lease for operations that replace the database file.
/// </summary>
public sealed class DatabaseMaintenanceCoordinator
{
    private const int Available = 0;
    private const int Maintenance = 1;
    private const int Disabled = 2;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _state;

    public bool IsMaintenanceActive => Volatile.Read(ref _state) == Maintenance;
    public bool AreOperationsDisabled => Volatile.Read(ref _state) == Disabled;
    public bool CanStartOperation => Volatile.Read(ref _state) == Available;

    internal IDisposable EnterOperation(CancellationToken cancellationToken = default)
    {
        ThrowIfDisabled();
        _gate.Wait(cancellationToken);
        if (Volatile.Read(ref _state) == Disabled)
        {
            _gate.Release();
            ThrowIfDisabled();
        }

        return new OperationLease(_gate);
    }

    public DatabaseMaintenanceLease EnterMaintenance(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisabled();
        _gate.Wait(cancellationToken);
        if (Interlocked.CompareExchange(ref _state, Maintenance, Available) != Available)
        {
            _gate.Release();
            ThrowIfDisabled();
            throw new InvalidOperationException("Database maintenance is already active.");
        }

        return new DatabaseMaintenanceLease(this);
    }

    internal void VerifyLease(DatabaseMaintenanceLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!ReferenceEquals(lease.Owner, this) || lease.IsDisposed)
        {
            throw new InvalidOperationException("The database maintenance lease is not active.");
        }
    }

    internal void DisableOperations(DatabaseMaintenanceLease lease)
    {
        VerifyLease(lease);
        Interlocked.Exchange(ref _state, Disabled);
    }

    internal void ExitMaintenance(DatabaseMaintenanceLease lease)
    {
        if (!ReferenceEquals(lease.Owner, this))
        {
            return;
        }

        Interlocked.CompareExchange(ref _state, Available, Maintenance);
        _gate.Release();
    }

    private void ThrowIfDisabled()
    {
        if (AreOperationsDisabled)
        {
            throw new DatabaseOperationsDisabledException();
        }
    }

    private sealed class OperationLease : IDisposable
    {
        private SemaphoreSlim? _gate;

        public OperationLease(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}

public sealed class DatabaseMaintenanceLease : IDisposable
{
    internal DatabaseMaintenanceLease(DatabaseMaintenanceCoordinator owner)
        => Owner = owner;

    internal DatabaseMaintenanceCoordinator Owner { get; }
    internal bool IsDisposed { get; private set; }

    public void DisableOperations() => Owner.DisableOperations(this);

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        Owner.ExitMaintenance(this);
    }
}

public sealed class DatabaseOperationsDisabledException : InvalidOperationException
{
    public DatabaseOperationsDisabledException()
        : base("Database operations are disabled. Exit KeeStashDock before continuing.")
    {
    }
}
