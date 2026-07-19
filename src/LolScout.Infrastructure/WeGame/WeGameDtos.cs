namespace LolScout.Infrastructure.WeGame;

using LolScout.Infrastructure.League;

public sealed record WeGameResponse(int StatusCode, string Content);

public interface IWeGameHttpTransport
{
    Task<WeGameResponse> GetAsync(Uri uri, ReadOnlyMemory<char> token, CancellationToken cancellationToken);
}

public sealed class WeGameHttpTransport : IWeGameHttpTransport
{
    private static readonly HashSet<int> RetriableStatuses = [408, 429, 502, 503, 504];
    private readonly ILeagueHttpTransport inner;
    private readonly TimeSpan timeout;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    public WeGameHttpTransport(ILeagueHttpTransport inner)
        : this(inner, TimeSpan.FromSeconds(15), Task.Delay) { }

    public WeGameHttpTransport(ILeagueHttpTransport inner, TimeSpan timeout, Func<TimeSpan, CancellationToken, Task> delay)
    {
        this.inner = inner;
        this.timeout = timeout;
        this.delay = delay;
    }

    public async Task<WeGameResponse> GetAsync(Uri uri, ReadOnlyMemory<char> token, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) await delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                var content = await inner.GetStringAsync(uri, token, timeoutSource.Token);
                return new(200, content);
            }
            catch (CertificatePinMismatchException) { throw; }
            catch (HttpRequestException ex) when (ex.StatusCode is not null)
            {
                var status = (int)ex.StatusCode.Value;
                if (attempt < 2 && RetriableStatuses.Contains(status)) continue;
                return new(status, "");
            }
            catch (HttpRequestException) when (attempt < 2) { continue; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2) { continue; }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("The local client request timed out.", ex); }
        }
        throw new InvalidOperationException("Retry loop completed unexpectedly.");
    }
}
