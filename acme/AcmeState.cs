using System;
using DnsClient.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace CertsServer.Acme;

public interface IAcmeState
{
    bool Completed { get; }
    Task<IAcmeState> MoveNextAsync(CancellationToken cancellationToken);
}

public sealed class TerminalState : IAcmeState
{
    public bool Completed => true;
    public Task<IAcmeState> MoveNextAsync(CancellationToken cancellationToken)
    {
        throw new OperationCanceledException("State terminated");
    }
}

public class AcmeStateMachineContext
{
    public IServiceProvider Services { get; }

    public AcmeStateMachineContext(IServiceProvider services)
    {
        this.Services = services;
    }
}

public abstract class AcmeState : IAcmeState
{
    public virtual bool Completed => false;
    protected readonly AcmeStateMachineContext _context;
    public AcmeState(AcmeStateMachineContext context)
    {
        this._context = context;
    }

    public abstract Task<IAcmeState> MoveNextAsync(CancellationToken cancellationToken);

    protected T MoveTo<T>() where T : IAcmeState
    {
        return _context.Services.GetRequiredService<T>();
    }
}

public abstract class SyncAcmeState : AcmeState
{
    protected SyncAcmeState(AcmeStateMachineContext context) : base(context) { }

    public override Task<IAcmeState> MoveNextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var next = MoveNext();

        return Task.FromResult(next);
    }

    public abstract IAcmeState MoveNext();
}
