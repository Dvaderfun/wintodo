using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml.Media;

namespace Wintodo.Controls;

internal sealed class ScrollPerformanceProbe
{
    private const int WarmupFrames = 120;
    private static bool _started;
    private readonly string _resultPath;
    private long _lastTimestamp;
    private long _sampleStartTimestamp;
    private int _totalFrames;
    private int _sampleFrames;
    private int _droppedFrames;
    private double _maxFrameMilliseconds;
    private long _callbackAllocatedBytes;

    private ScrollPerformanceProbe(string resultPath)
    {
        _resultPath = resultPath;
    }

    internal static void TryStart(int itemCount)
    {
        if (_started)
        {
            return;
        }

        string? resultPath =
            App.GetCommandLineOption("--perf-scroll-result");
        string? expectedText =
            App.GetCommandLineOption("--perf-scroll-items");
        int expectedItems = int.TryParse(
            expectedText,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int parsed)
            ? parsed
            : 10_000;
        if (string.IsNullOrWhiteSpace(resultPath) || itemCount < expectedItems)
        {
            return;
        }

        _started = true;
        string fullResultPath = Path.GetFullPath(resultPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullResultPath)
                ?? throw new InvalidOperationException("Result path requires a parent."));
        File.WriteAllText(fullResultPath + ".ready", itemCount.ToString(
            CultureInfo.InvariantCulture));
        var probe = new ScrollPerformanceProbe(fullResultPath);
        CompositionTarget.Rendering += probe.OnRendering;
    }

    private void OnRendering(object? sender, object args)
    {
        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
        long now = Stopwatch.GetTimestamp();
        bool shouldFinish = false;
        _totalFrames++;

        if (_totalFrames == WarmupFrames)
        {
            _sampleStartTimestamp = now;
            _lastTimestamp = now;
        }
        else if (_totalFrames > WarmupFrames)
        {
            double frameMilliseconds =
                Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalMilliseconds;
            _lastTimestamp = now;
            _sampleFrames++;
            _maxFrameMilliseconds = Math.Max(
                _maxFrameMilliseconds,
                frameMilliseconds);
            if (frameMilliseconds > 25)
            {
                _droppedFrames++;
            }

            if (Stopwatch.GetElapsedTime(_sampleStartTimestamp, now)
                    >= TimeSpan.FromSeconds(6))
            {
                shouldFinish = true;
            }
        }

        _callbackAllocatedBytes +=
            GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        if (shouldFinish)
        {
            Finish(now);
        }
    }

    private void Finish(long now)
    {
        CompositionTarget.Rendering -= OnRendering;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(_sampleStartTimestamp, now);
        double framesPerSecond = _sampleFrames / elapsed.TotalSeconds;
        Directory.CreateDirectory(
            Path.GetDirectoryName(_resultPath)
                ?? throw new InvalidOperationException("Result path requires a parent."));
        string json = $$"""
            {
              "sampleSeconds": {{elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)}},
              "frames": {{_sampleFrames}},
              "framesPerSecond": {{framesPerSecond.ToString("F3", CultureInfo.InvariantCulture)}},
              "droppedFrames": {{_droppedFrames}},
              "maxFrameMilliseconds": {{_maxFrameMilliseconds.ToString("F3", CultureInfo.InvariantCulture)}},
              "callbackAllocatedBytes": {{_callbackAllocatedBytes}}
            }
            """;
        File.WriteAllText(_resultPath, json);
    }
}
