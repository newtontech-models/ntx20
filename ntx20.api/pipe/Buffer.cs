using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ntx20.api.pipe
{
    
    public static class Buffer
    {
        public static async IAsyncEnumerable<T> RunWithBuffer<T>(this IAsyncEnumerable<T> source, int capacity = 0 ,[EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using BlockingCollection<T> bc = capacity > 0 ? new BlockingCollection<T>(capacity) : new BlockingCollection<T>();
            var writer = Task.Run(async () =>
            {
                await foreach (T v in source.WithCancellation(cancellationToken))
                {
                    bc.Add(v);
                }
                bc.CompleteAdding();
            });
            
            foreach (var x in bc.GetConsumingEnumerable())
            {
                yield return x;
            }

            await writer;



        }
    }
}
