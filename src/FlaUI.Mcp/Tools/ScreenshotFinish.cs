using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>Steps 4 and 5 of windows_screenshot: judge and annotate the capture, then write the result.</summary>
public partial class ScreenshotTool
{
    /// <summary>What the guards found about a capture before it is returned.</summary>
    private sealed record ShotJudgement(
        ScreenshotChecks.CaptureVerdict Verdict,
        Win32Desktop.OcclusionCheck? Occlusion,
        uint? WindowDpi,
        uint? MonitorDpi,
        string? DpiWarning
    );

    private McpToolResult Finish(ShotRequest request, Shot shot, byte[] bytes)
    {
        var judgement = Judge(request, shot);
        if (judgement.Verdict.Refusal is { } refusal)
        {
            return ErrorResult(refusal);
        }

        var verdict = judgement.Verdict;
        if (shot.ParkWarning != null)
        {
            verdict = verdict with { Warnings = [shot.ParkWarning, .. verdict.Warnings] };
        }

        List<ScreenshotChecks.LocatedElement>? located = null;
        if (
            request.LocateRefs.Length > 0
            && Locate(request, shot, bytes, judgement.DpiWarning, out located) is { } error
        )
        {
            return error;
        }

        var result = BuildScreenshotResult(
            bytes,
            request.NormalizedSavePath,
            request.Overwrite,
            request.IncludeImage,
            request.Frame
        );
        if (result.IsError == true)
        {
            return result;
        }

        if (shot.ParkProof != null)
        {
            result = result with
            {
                Outcome = (result.Outcome ?? new ToolOutcome("completed", "not-tracked")).WithProof(shot.ParkProof),
            };
        }
        // Warnings do not depend on includeMetadata: a warning the caller has to opt into is
        // not a warning, and the default call is the one that produced a mis-scaled capture.
        foreach (var warning in verdict.Warnings)
        {
            result.Content.Add(new McpContent { Text = warning });
        }
        // locate output lives in the metadata block, and no caller wants the rectangles but
        // not the metadata, so asking for locate turns it on rather than being refused.
        if (!request.IncludeMetadata && located == null)
        {
            return result;
        }

        Annotate(result, request, shot, bytes, judgement, located);
        return result;
    }

    private static ShotJudgement Judge(ShotRequest request, Shot shot)
    {
        var windowDpi = DpiUtility.WindowDpi(shot.TargetHwnd);
        var monitorDpi = DpiUtility.MonitorDpi(shot.TargetHwnd);
        var dpiWarning = DpiUtility.DescribeMismatch(windowDpi, monitorDpi);
        // Screen-pixel capture reads the glass, so anything layered over the target is in
        // the image. Native capture renders the window's own surface and is exempt; so is a
        // capture with no known target window, which is reported as unchecked rather than clean.
        Win32Desktop.OcclusionCheck? occlusion = null;
        var guardTarget = shot.TargetHwnd;
        // An element's NativeWindowHandle is 0 for most WPF controls, which left every
        // element capture on the screen-pixel path with no guard at all. The window under
        // the captured region is the target in that case.
        if (guardTarget == 0 && shot.SourceBounds is { } region && ScreenshotChecks.ReadsScreenPixels(shot.Method))
        {
            guardTarget = InputDesktop.Current.WindowAt(
                new System.Drawing.Point(region.Left + region.Width / 2, region.Top + region.Height / 2)
            );
        }

        if (guardTarget != 0 && shot.SourceBounds is { } captured && ScreenshotChecks.ReadsScreenPixels(shot.Method))
        {
            occlusion = Win32Desktop.FindObstructions(
                Win32Desktop.ObserveZOrder(),
                guardTarget,
                InputDesktop.Current.ProcessOf(guardTarget),
                ScreenshotChecks.GuardRect(captured, request.Frame)
            );
        }

        var verdict = ScreenshotChecks.Decide(
            shot.Method,
            shot.OcclusionExempt,
            occlusion,
            dpiWarning,
            request.AllowOccludedPixels,
            ScreenshotChecks.UncheckedReason(shot.Method, guardTarget, shot.SourceBounds)
        );
        return new ShotJudgement(verdict, occlusion, windowDpi, monitorDpi, dpiWarning);
    }

    /// <summary>The located refs' rectangles in the image, or the refusal when they would not map 1:1.</summary>
    private McpToolResult? Locate(
        ShotRequest request,
        Shot shot,
        byte[] bytes,
        string? dpiWarning,
        out List<ScreenshotChecks.LocatedElement>? located
    )
    {
        located = null;
        using var probe = new MemoryStream(bytes);
        using var measured = System.Drawing.Image.FromStream(probe);
        // A located rectangle only means anything if the image is a faithful 1:1 render
        // of the region sourceBounds names.
        if (shot.SourceBounds is not { } origin)
        {
            return ErrorResult("locate needs a known capture origin, which this capture did not record.");
        }

        if (dpiWarning != null)
        {
            return ErrorResult(
                "locate is unavailable while window and monitor DPI disagree: the "
                    + $"coordinates would be computed against a mis-scaled render. {dpiWarning}"
            );
        }

        if (measured.Width != origin.Width || measured.Height != origin.Height)
        {
            return ErrorResult(
                $"locate is unavailable: the capture is {measured.Width}x{measured.Height} "
                    + $"but its source region is {origin.Width}x{origin.Height}, so image pixels do not map "
                    + "1:1 to screen coordinates."
            );
        }

        var found = new List<ScreenshotChecks.LocatedElement>();
        foreach (var reference in request.LocateRefs)
        {
            var refHwnd = _sessionManager.GetWindowHwnd(_elementRegistry.WindowForRef(reference));
            // Both zero would compare equal and wave through an unrelated ref.
            if (refHwnd == 0 || shot.TargetHwnd == 0 || refHwnd != shot.TargetHwnd)
            {
                return ErrorResult(
                    $"locate ref {reference} belongs to a different window than the "
                        + "capture, so its coordinates would not be in this image."
                );
            }

            if (
                _elementRegistry.GetElement(reference) is not { } element
                || !element.Properties.BoundingRectangle.TryGetValue(out var elementBounds)
            )
            {
                return ErrorResult($"locate ref {reference} did not report bounds.");
            }

            found.Add(
                ScreenshotChecks.LocateInCapture(
                    reference,
                    elementBounds,
                    origin,
                    measured.Width,
                    measured.Height,
                    request.Frame
                )
            );
        }

        located = found;
        return null;
    }

    /// <summary>Add the metadata block: method, bounds, sizes, DPI and occlusion flags, located refs.</summary>
    private static void Annotate(
        McpToolResult result,
        ShotRequest request,
        Shot shot,
        byte[] bytes,
        ShotJudgement judgement,
        List<ScreenshotChecks.LocatedElement>? located
    )
    {
        using var stream = new MemoryStream(bytes);
        using var bitmap = System.Drawing.Image.FromStream(stream);
        var occlusion = judgement.Occlusion;
        var metadata = new
        {
            method = shot.Method,
            sourceBounds = shot.SourceBounds is { } b
                ? new
                {
                    x = b.X,
                    y = b.Y,
                    width = b.Width,
                    height = b.Height,
                }
                : null,
            sourceWidth = bitmap.Width,
            sourceHeight = bitmap.Height,
            width = request.Frame?.Width ?? bitmap.Width,
            height = request.Frame?.Height ?? bitmap.Height,
            frame = request.Frame,
            windowDpi = judgement.WindowDpi,
            monitorDpi = judgement.MonitorDpi,
            // Flags, not prose: screenFallback:false read as reassurance and stopped nothing.
            dpiMismatch = judgement.DpiWarning != null,
            // Tri-state. Null means the capture could not be checked, which is not the same
            // as checked and clean, and must not read as reassurance either.
            occluded = occlusion is null ? (bool?)null : !occlusion.IsClean,
            occlusionWarning = ScreenshotChecks.DescribeOcclusion(occlusion),
            located,
            screenFallback = shot.Method == "screen-fallback",
        };
        result.Content.Add(new McpContent { Text = JsonSerializer.Serialize(metadata, McpProtocol.JsonOptions) });
    }
}
