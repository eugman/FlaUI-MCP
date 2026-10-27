using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

/// <summary>windows_screenshot's pure checks: where to locate, what the guards decide, where to save.</summary>
internal static class ScreenshotChecks
{
    /// <summary>An element's rectangle as measured in the image that was returned.</summary>
    /// <param name="Ref">The element ref this describes.</param>
    /// <param name="X">Left edge in image pixels; negative when the element starts outside the crop.</param>
    /// <param name="Y">Top edge in image pixels.</param>
    /// <param name="Width">Width in image pixels.</param>
    /// <param name="Height">Height in image pixels.</param>
    /// <param name="Clipped">Whether any part falls outside the returned image.</param>
    internal sealed record LocatedElement(string Ref, int X, int Y, int Width, int Height, bool Clipped);

    /// <summary>
    /// Convert a screen-space element rectangle into the coordinate space of the image actually
    /// returned. The caller reads element bounds after capturing pixels, so this translation
    /// does not prove the element stayed still between those reads.
    /// Deliberately never clamps: an element outside the crop is reported as outside rather than
    /// moved to the edge, because a plausible-looking wrong rectangle is what put annotation boxes
    /// on the wrong control. The title-bar offset this removes is not a constant — it
    /// was 39px in one DPI state and 55px in another on the same window (30).
    /// </summary>
    /// <param name="reference">The element ref being described.</param>
    /// <param name="element">The element's screen rectangle.</param>
    /// <param name="sourceBounds">Screen rectangle the capture covers; its origin is image pixel (0,0).</param>
    /// <param name="imageWidth">Width of the captured bitmap, before any crop.</param>
    /// <param name="imageHeight">Height of the captured bitmap, before any crop.</param>
    /// <param name="frame">The crop applied to the capture, if any.</param>
    /// <returns>The element's rectangle in the returned image.</returns>
    internal static LocatedElement LocateInCapture(
        string reference,
        System.Drawing.Rectangle element,
        System.Drawing.Rectangle sourceBounds,
        int imageWidth,
        int imageHeight,
        CaptureFrame? frame
    )
    {
        // Screen -> capture, then capture -> returned image when a crop was applied.
        var x = element.X - sourceBounds.X - (frame?.X ?? 0);
        var y = element.Y - sourceBounds.Y - (frame?.Y ?? 0);
        var visible = new System.Drawing.Rectangle(0, 0, frame?.Width ?? imageWidth, frame?.Height ?? imageHeight);
        var located = new System.Drawing.Rectangle(x, y, element.Width, element.Height);
        return new(reference, x, y, element.Width, element.Height, !visible.Contains(located));
    }

    /// <summary>
    /// The screen rectangle the occlusion guard checks: the part of the capture a crop keeps. A window
    /// over pixels the frame throws away cannot reach the returned image, so it must not refuse it.
    /// </summary>
    /// <param name="sourceBounds">Screen rectangle the capture covers; its origin is image pixel (0,0).</param>
    /// <param name="frame">The crop applied to the capture, if any.</param>
    /// <returns>The kept region in screen coordinates.</returns>
    internal static System.Drawing.Rectangle GuardRect(System.Drawing.Rectangle sourceBounds, CaptureFrame? frame)
    {
        if (frame == null)
        {
            return sourceBounds;
        }

        var kept = new System.Drawing.Rectangle(
            sourceBounds.X + frame.X,
            sourceBounds.Y + frame.Y,
            frame.Width,
            frame.Height
        );
        kept.Intersect(sourceBounds);
        return kept;
    }

    /// <summary>Whether a capture method reads pixels off the screen rather than the window's own surface.</summary>
    /// <param name="method">The capture method that produced the image.</param>
    /// <returns>True when anything layered over the target would be in the image.</returns>
    internal static bool ReadsScreenPixels(string method) => method is "screen-uia-bounds" or "screen-fallback";

    /// <summary>
    /// Say why a screen-pixel capture could not be checked for obstructions, so "unchecked" is
    /// never reported as "clean". Native capture renders the window's own surface and needs no
    /// check, so it has no reason to state.
    /// </summary>
    /// <param name="method">How the pixels were obtained.</param>
    /// <param name="targetHwnd">The window the guard would bound the z-order by, or 0.</param>
    /// <param name="bounds">The screen rectangle read, or null when none was recorded.</param>
    /// <returns>A warning, or null when the capture was checkable or needed no check.</returns>
    internal static string? UncheckedReason(string method, nint targetHwnd, System.Drawing.Rectangle? bounds)
    {
        if (!ReadsScreenPixels(method))
        {
            return null;
        }

        if (targetHwnd == 0)
        {
            return "This capture read screen pixels but no target window could be identified, so it "
                + "was not checked for windows covering it. Anything on top is in the image.";
        }

        if (bounds is null)
        {
            return "This capture read screen pixels but recorded no source rectangle, so it was not "
                + "checked for windows covering it. Anything on top is in the image.";
        }

        return null;
    }

    /// <summary>
    /// What to do about a capture, and what to tell the caller regardless.
    /// </summary>
    /// <param name="Refusal">Why no image may be returned, or null to proceed.</param>
    /// <param name="Warnings">Statements that must reach the caller even on success.</param>
    internal sealed record CaptureVerdict(string? Refusal, IReadOnlyList<string> Warnings);

    /// <summary>
    /// Decide whether a capture can be trusted. Extracted from the capture path so the choices that
    /// matter are testable without a desktop: that a DPI mismatch warns and never refuses, that
    /// occlusion refuses unless the caller opted in or the path has no alternative, and that an
    /// unchecked capture is never reported as clean.
    /// </summary>
    /// <param name="method">The capture method that produced the image.</param>
    /// <param name="occlusionExempt">The path has a modal in front of the target by construction.</param>
    /// <param name="occlusion">The occlusion observation, or null when none was possible.</param>
    /// <param name="dpiWarning">A window/monitor DPI disagreement, or null.</param>
    /// <param name="allowOccludedPixels">The caller deliberately wants a partly covered window.</param>
    /// <returns>The refusal, if any, and every warning to surface.</returns>
    internal static CaptureVerdict Decide(
        string method,
        bool occlusionExempt,
        Win32Desktop.OcclusionCheck? occlusion,
        string? dpiWarning,
        bool allowOccludedPixels,
        string? uncheckedReason = null,
        ProcessPolicy? policy = null
    )
    {
        var warnings = new List<string>();
        // Unchecked is not clean. Skipping the guard used to produce a reply indistinguishable
        // from a verified one, which is the failure the guard exists to remove.
        if (uncheckedReason != null)
        {
            warnings.Add(uncheckedReason);
        }
        // DPI mismatch never refuses: a screen-pixel capture in that state returns literally what is
        // on the glass, which for photography is the correct result. DPI-unaware apps can
        // keep a mismatch until their scaling environment changes.
        if (dpiWarning != null)
        {
            warnings.Add(dpiWarning);
        }

        if (occlusion is null || occlusion.IsClean)
        {
            return new(null, warnings);
        }

        var finding = DescribeOcclusion(occlusion, policy)!;
        // The blocked-provider fallback exists precisely because a modal is up; refusing there would
        // leave it no way to capture, and strictNative is already unavailable on that path.
        if (occlusionExempt || allowOccludedPixels || !ReadsScreenPixels(method))
        {
            warnings.Add(finding);
            return new(null, warnings);
        }

        return new(
            finding
                + " No screenshot was returned. Use strictNative:true to capture the window's "
                + "own surface instead (unaffected by what is on top), move the covering window, or pass "
                + "allowOccludedPixels:true to capture the screen anyway.",
            warnings
        );
    }

    /// <summary>
    /// State what was covering a capture, with no claim about what was returned: the same finding is
    /// attached both to a refusal and to an image the caller asked for anyway.
    /// </summary>
    /// <param name="occlusion">The observation to describe, or null.</param>
    /// <param name="policy">Redacts an obstruction whose owner is unknown or outside a hard app allowlist.</param>
    /// <returns>The finding, or null when the capture was clean or unchecked.</returns>
    internal static string? DescribeOcclusion(Win32Desktop.OcclusionCheck? occlusion, ProcessPolicy? policy = null)
    {
        if (occlusion is null || occlusion.IsClean)
        {
            return null;
        }

        if (!occlusion.TargetFound)
        {
            return "The capture target was not found in the desktop window order, so it could not be "
                + "shown to be unobstructed.";
        }

        policy ??= ProcessPolicy.AllowAll;
        var named = occlusion.Obstructions.Select(w =>
        {
            if (policy.IsRestricted && (w.ProcessId == 0 || !policy.IsProcessAllowed(w.ProcessId)))
            {
                return "window outside the app allowlist (details hidden)";
            }

            var title = string.IsNullOrEmpty(w.Title) ? "untitled" : w.Title;
            return $"\"{title}\" (HWND {w.Hwnd}, PID {w.ProcessId})";
        });
        return "The capture area is covered or may be covered by another window; screen pixels may not "
            + $"show only the target: {string.Join("; ", named)}.";
    }

    internal static T CaptureForeground<T>(int processId, ProcessPolicy policy, Func<T> capture)
    {
        if (!policy.IsProcessAllowed(processId))
        {
            var name = InputDesktop.Current.ProcessNameOf(processId) ?? "unknown";
            throw new UnauthorizedAccessException(policy.DescribeDenied($"The foreground window's process '{name}'"));
        }

        return capture();
    }

    internal static bool TryNormalizeSavePath(
        string? savePath,
        bool overwrite,
        out string? normalizedPath,
        out string error
    )
    {
        normalizedPath = null;
        error = "";

        if (string.IsNullOrWhiteSpace(savePath))
        {
            return true;
        }

        if (!Path.IsPathFullyQualified(savePath))
        {
            error = $"savePath must be an absolute local path: {savePath}";
            return false;
        }

        if (savePath.StartsWith(@"\\") || savePath.StartsWith(@"\\?\") || savePath.StartsWith(@"\\.\"))
        {
            error = "savePath must be a local drive path; UNC and device paths are not allowed";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(savePath);
        }
        catch (Exception ex)
        {
            error = $"savePath is invalid: {ex.Message}";
            return false;
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            error = "savePath must end with .png";
            return false;
        }

        if (File.Exists(fullPath) && !overwrite)
        {
            error = $"savePath already exists; pass overwrite=true to replace it: {fullPath}";
            return false;
        }

        normalizedPath = fullPath;
        return true;
    }

    /// <summary>Write the PNG to a temporary file beside the target, then move it into place; the error, or null.</summary>
    internal static string? SaveAtomically(string savePath, byte[] imageData, bool overwrite)
    {
        try
        {
            var directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = Path.Combine(
                directory ?? Directory.GetCurrentDirectory(),
                $"{Path.GetFileName(savePath)}.{Guid.NewGuid():N}.tmp"
            );
            try
            {
                OperationContext.Check();
                File.WriteAllBytes(tempPath, imageData);
                OperationContext.Check();
                File.Move(tempPath, savePath, overwrite);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            return $"Failed to save screenshot to {savePath}: {ex.Message}";
        }
    }

    /// <summary>
    /// A capture that parked the pointer first: the capture is a read that succeeded, and the park is its own
    /// detail (<c>details.park</c>). A verified park is also the outcome's proof (status ok). One that left the pointer
    /// over the window is not: it makes the capture unverified with a warning (a hover state may show), never failed,
    /// so it does not read as a failure after input. A refused park moved nothing and only adds the detail.
    /// </summary>
    internal static McpToolResult WithPark(McpToolResult result, Proof park)
    {
        var outcome = (result.Outcome ?? new ToolOutcome("completed", "not-tracked")) with
        {
            Details = new Dictionary<string, JsonElement>
            {
                ["park"] = JsonSerializer.SerializeToElement(park, McpProtocol.JsonOptions),
            },
        };
        if (park.Verdict is Proof.Verified or Proof.NoOp)
        {
            return result with { Outcome = outcome.WithProof(park) };
        }

        var content = result.Content.ToList();
        if (park.Verdict == Proof.Mismatch)
        {
            content.Add(
                new McpContent
                {
                    Text =
                        $"parkPointer moved the pointer, but it is still over the window (at {park.Observed}); a hover "
                        + "state or tooltip may show in the capture.",
                }
            );
        }

        return result with
        {
            Outcome = outcome,
            Content = content,
        };
    }
}
