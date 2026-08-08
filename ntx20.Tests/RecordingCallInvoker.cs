using Grpc.Core;

namespace ntx20.Tests;

internal sealed class RecordingCallInvoker : CallInvoker
{
    public int DuplexStreamingCallCount { get; private set; }
    public string MethodName { get; private set; }
    public MethodType? MethodType { get; private set; }
    public Metadata Headers { get; private set; }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string host,
        CallOptions options)
    {
        DuplexStreamingCallCount++;
        MethodName = method.FullName;
        MethodType = method.Type;
        Headers = options.Headers;

        return new AsyncDuplexStreamingCall<TRequest, TResponse>(
            new RecordingClientStreamWriter<TRequest>(),
            new EmptyAsyncStreamReader<TResponse>(),
            Task.FromResult(new Metadata()),
            () => new Status(StatusCode.OK, string.Empty),
            () => new Metadata(),
            () => { });
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string host,
        CallOptions options,
        TRequest request)
    {
        throw new NotSupportedException();
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string host,
        CallOptions options,
        TRequest request)
    {
        throw new NotSupportedException();
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string host,
        CallOptions options,
        TRequest request)
    {
        throw new NotSupportedException();
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string host,
        CallOptions options)
    {
        throw new NotSupportedException();
    }

    private sealed class RecordingClientStreamWriter<T> : IClientStreamWriter<T>
    {
        public WriteOptions WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            return Task.CompletedTask;
        }

        public Task CompleteAsync()
        {
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyAsyncStreamReader<T> : IAsyncStreamReader<T>
    {
        public T Current => default;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }
    }
}
