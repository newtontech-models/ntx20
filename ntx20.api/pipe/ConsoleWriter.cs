using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using ntx20.api.proto;

#nullable enable

namespace ntx20.api.pipe;

public sealed class ConsoleWriter : IAsyncSink<Payload>
{
    private readonly string track;
    private readonly bool timed;
    private readonly Regex capitalizeRegex = new(@"^(\s*)(\S)(.*)$", RegexOptions.Compiled);

    private readonly SemaphoreSlim sync = new(1, 1);
    private readonly StringBuilder committedMarkup = new();
    private string lookaheadMarkup = string.Empty;

    private double lastPrintTime = -10000.0;
    private double lastCommittedTextTimestamp = -10000.0;
    private bool separatorPrinted;
    private double lastTimestamp;
    private string? currentSpeaker;

    private DateTime? playbackAnchor;
    private double playbackAnchorMs;
    private double lastRenderedTimestamp = double.MinValue;

    private readonly AutoResetEvent liveSignal = new(false);
    private readonly CancellationTokenSource liveCts = new();
    private Task? liveTask;
    private volatile string pendingLiveMarkup = string.Empty;
    private bool liveDisabled;
    private bool clearedOnce;

    public ConsoleWriter(string track, bool timed = false)
    {
        this.track = track;
        this.timed = timed;
    }

    public async Task WriteAsync(Payload item, CancellationToken cancelationToken = default)
    {
        if (!string.Equals(item.Track, track, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var committedBuilder = new StringBuilder();
        var lookaheadBuilder = new StringBuilder();
        double chunkTimestamp = lastTimestamp;

        foreach (var chunk in item.Chunk)
        {
            if (chunk.Key == "ts" && !chunk.Tags.Contains("la"))
            {
                chunkTimestamp = chunk.D;
                lastTimestamp = chunkTimestamp;
            }
            else if (chunk.Key == "txt" && !chunk.Tags.Contains("noise"))
            {
                var text = chunk.S ?? string.Empty;
                if (chunk.Tags.Contains("sos"))
                {
                    text = capitalizeRegex.Replace(text, m =>
                        m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant() + m.Groups[3].Value);
                }

                if (chunk.Tags.Contains("la"))
                {
                    lookaheadBuilder.Append(text);
                }
                else
                {
                    // Check for speaker label change
                    if (chunk.Labels.TryGetValue("spk", out var spkLabel) && spkLabel != currentSpeaker)
                    {
                        currentSpeaker = spkLabel;
                        committedBuilder.Append($"\n@{spkLabel}: ");
                    }
                    
                    // Check if we should insert a separator before this committed text
                    if (!separatorPrinted && 
                        (chunkTimestamp - lastCommittedTextTimestamp) > 5000 && 
                        committedBuilder.Length == 0 &&
                        committedMarkup.Length > 0)
                    {
                        committedBuilder.Append("[grey]===[/]\n");
                        separatorPrinted = true;
                    }
                    
                    committedBuilder.Append(text);
                    lastCommittedTextTimestamp = chunkTimestamp;
                    separatorPrinted = false;
                }
            }
        }

        var committed = committedBuilder.ToString();
        var lookahead = lookaheadBuilder.ToString();

        if ((committed + lookahead).Length == 0)
        {
            return;
        }

        if (timed && chunkTimestamp >= 0)
        {
            await DelayForTimestamp(chunkTimestamp, cancelationToken).ConfigureAwait(false);
        }

        await sync.WaitAsync(cancelationToken).ConfigureAwait(false);
        try
        {
            if (committed.Length > 0)
            {
                // Ensure newline before separator if committed text already contains the separator
                if (committed.Contains("[grey]===[/]") && committedMarkup.Length > 0 && committedMarkup[^1] != '\n')
                {
                    committedMarkup.Append('\n');
                }
                committedMarkup.Append(committed);
            }

            lookaheadMarkup = BuildLookaheadMarkup(lookahead);
            lastPrintTime = chunkTimestamp;

            Render();
        }
        finally
        {
            sync.Release();
        }
    }

    public async Task CompleteAsync(CancellationToken cancelationToken = default)
    {
        await sync.WaitAsync(cancelationToken).ConfigureAwait(false);
        try
        {
            lookaheadMarkup = string.Empty;
            Render();

            if (!separatorPrinted)
            {
                if (committedMarkup.Length > 0 && committedMarkup[^1] != '\n')
                {
                    committedMarkup.Append('\n');
                }
                committedMarkup.Append("[grey]===[/]");
                separatorPrinted = true;
                Render();
            }
        }
        finally
        {
            sync.Release();
        }

        await StopLiveAsync().ConfigureAwait(false);
    }

    public Task FlushAsync(CancellationToken cancelationToken = default)
    {
        return Task.CompletedTask;
    }

    private void Render()
    {
        ClearConsoleOnce();

        var full = committedMarkup.ToString() + lookaheadMarkup;
        if (TryRenderLive(full))
        {
            return;
        }

        // Fallback (e.g. Live cannot start): rewrite in-place.
        TryHomeAndClearToEnd();
        if (full.Length > 0)
        {
            AnsiConsole.Write(new Markup(full));
        }
    }

    private bool TryRenderLive(string markup)
    {
        if (liveDisabled)
        {
            return false;
        }

        EnsureLiveStarted();
        if (liveTask == null)
        {
            return false;
        }

        pendingLiveMarkup = markup;
        liveSignal.Set();
        return true;
    }

    private void EnsureLiveStarted()
    {
        if (liveTask != null || liveDisabled)
        {
            return;
        }

        ClearConsoleOnce();

        // Live rendering requires an interactive console. If this fails (redirected output, CI, etc),
        // we fall back to the simple clear+repaint renderer.
        liveTask = Task.Run(() =>
        {
            try
            {
                AnsiConsole.Live(new Markup(string.Empty)).Start(ctx =>
                {
                    while (!liveCts.IsCancellationRequested)
                    {
                        // Wait briefly; coalesce multiple writes into one refresh.
                        liveSignal.WaitOne(100);
                        if (liveCts.IsCancellationRequested)
                        {
                            break;
                        }

                        var current = pendingLiveMarkup;
                        ctx.UpdateTarget(new Markup(current));
                        ctx.Refresh();
                    }

                    // Ensure the last state is shown.
                    var last = pendingLiveMarkup;
                    if (!string.IsNullOrEmpty(last))
                    {
                        ctx.UpdateTarget(new Markup(last));
                        ctx.Refresh();
                    }
                });
            }
            catch
            {
                liveDisabled = true;
                liveTask = null;
            }
        });
    }

    private void ClearConsoleOnce()
    {
        if (clearedOnce)
        {
            return;
        }

        clearedOnce = true;

        try
        {
            AnsiConsole.Clear();
            return;
        }
        catch
        {
            // ignored
        }

        try
        {
            Console.Clear();
            return;
        }
        catch
        {
            // ignored
        }

        try
        {
            // ANSI: clear screen + move cursor to home.
            Console.Write("\x1b[2J\x1b[H");
        }
        catch
        {
            // ignored
        }
    }

    private static void TryHomeAndClearToEnd()
    {
        try
        {
            // ANSI: move cursor to home + clear from cursor to end of screen.
            Console.Write("\x1b[H\x1b[J");
        }
        catch
        {
            // ignored
        }
    }

    private async Task StopLiveAsync()
    {
        if (liveTask == null)
        {
            return;
        }

        try
        {
            liveCts.Cancel();
            liveSignal.Set();
            await liveTask.ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }
        finally
        {
            liveTask = null;
        }
    }

    private static string BuildLookaheadMarkup(string lookahead)
    {
        if (string.IsNullOrEmpty(lookahead))
        {
            return string.Empty;
        }

        return "[grey50]" + Markup.Escape(lookahead) + "[/]";
    }

    private async Task DelayForTimestamp(double targetMs, CancellationToken cancellationToken)
    {
        if (targetMs <= lastRenderedTimestamp)
        {
            return;
        }

        if (playbackAnchor == null)
        {
            playbackAnchor = DateTime.UtcNow;
            playbackAnchorMs = targetMs;
        }

        var targetTime = playbackAnchor.Value + TimeSpan.FromMilliseconds(targetMs - playbackAnchorMs);
        var delay = targetTime - DateTime.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        lastRenderedTimestamp = targetMs;
    }

    Task IAsyncSink<Payload>.WriteAsync(Payload item, CancellationToken cancelationToken)
        => WriteAsync(item, cancelationToken);

    Task IAsyncSink<Payload>.CompleteAsync(CancellationToken cancelationToken)
        => CompleteAsync(cancelationToken);

    Task IAsyncSink<Payload>.FlushAsync(CancellationToken cancelationToken)
        => FlushAsync(cancelationToken);
}
