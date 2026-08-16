using Microsoft.Extensions.Hosting;
using Proxytrace.Domain;
using Proxytrace.Domain.AuditLog;

namespace Proxytrace.Application.AuditLog.Internal;

/// <summary>
/// Consumer side of audit capture: drains the <see cref="IAuditChannel"/> and persists each entry as
/// an <see cref="IAuditLogEntry"/>, stamping <c>CreatedAt</c> with the captured event time. Failures
/// are written to <see cref="Console.Error"/> only, never via <c>ILogger</c>. Retention is handled
/// separately by <see cref="AuditLogCleanupService"/>.
/// </summary>
internal sealed class AuditWriter : BackgroundService
{
    private readonly IAuditChannel channel;
    private readonly IAuditLogEntry.CreateExisting createEntry;
    private readonly IAuditLogRepository repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditWriter"/> class.
    /// </summary>
    public AuditWriter(
        IAuditChannel channel,
        IAuditLogEntry.CreateExisting createEntry,
        IAuditLogRepository repository)
    {
        this.channel = channel;
        this.createEntry = createEntry;
        this.repository = repository;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Outer loop keeps the consumer alive across transient persistence failures.
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await foreach (AuditCapture entry in channel.ReadAllAsync(cancellationToken))
                {
                    await PersistAsync(entry, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown: drain whatever is already buffered before exiting, so queued
                // audits still persist across a restart/deploy (the log is lossless). The stopping
                // token is already tripped, so persistence uses CancellationToken.None.
                await DrainRemainingAsync();
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AuditWriter] consumer loop failed; retrying shortly: {ex}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    // Best-effort: persist every entry still sitting in the channel at shutdown. By the time the
    // stopping token trips, the host has stopped accepting requests, so the producer side is quiescing
    // and the backlog is finite. Uses CancellationToken.None — the stopping token is already cancelled.
    private async Task DrainRemainingAsync()
    {
        while (channel.TryRead(out AuditCapture? entry))
        {
            await PersistAsync(entry, CancellationToken.None);
        }
    }

    private async Task PersistAsync(AuditCapture entry, CancellationToken cancellationToken)
    {
        try
        {
            // Persist with the captured event time as CreatedAt (not the later drain time), so the
            // row's timestamp reflects when the action actually happened even under writer lag.
            IAuditLogEntry domain = createEntry(
                entry.Action,
                entry.ActorType,
                entry.ActorUserId,
                entry.ActorEmail,
                entry.ActorApiKeyId,
                entry.ProjectId,
                entry.TargetType,
                entry.TargetId,
                entry.TargetLabel,
                entry.Details,
                entry.Outcome,
                new CapturedAuditData(Guid.NewGuid(), entry.OccurredAt, entry.OccurredAt));

            await repository.AddAsync(domain, cancellationToken);
        }
        catch (Exception ex)
        {
            // Console only — logging here would add noise/load while audit persistence is already failing.
            Console.Error.WriteLine($"[AuditWriter] failed to persist audit entry: {ex}");
        }
    }

    private sealed record CapturedAuditData(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
        : IDomainEntityData;
}
