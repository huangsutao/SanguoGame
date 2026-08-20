using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace SanguoGame.Server.Jobs;

internal sealed class NoopBackgroundJobClient : IBackgroundJobClient
{
    public string Create(Job job, IState state) => "noop";

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}
