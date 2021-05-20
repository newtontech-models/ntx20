using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.command
{
    public interface ICommand
    {
        Task<int> RunAsync(CancellationToken breaker);
    }
}
