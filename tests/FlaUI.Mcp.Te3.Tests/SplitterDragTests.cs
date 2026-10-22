using System.Drawing;
using System.Text.Json;
using Xunit;

/// <summary>
/// The resize drag Core is asked for (review of 443d347): every caption in view, the resized pane's own included
/// (a drag that starts on it floats the pane), goes in fromMustNotHit, so Core refuses a start on any of them
/// before the button goes down. Pure; no desktop access.
/// </summary>
public sealed class SplitterDragTests
{
    [Fact]
    public void EveryCaptionIncludingTheResizedPanesOwnIsAvoided()
    {
        var parts = new (string Title, string? Caption)[]
        {
            ("TOM Explorer", "caption-tom"),
            ("Properties", "caption-properties"),
            ("DAX Query 1", "tab-dax"),
            ("Untitled", null),
        };

        Assert.Equal(["caption-tom", "caption-properties", "tab-dax"], SplitterPlan.Avoided(parts));
    }

    [Fact]
    public void TheDragAsksCoreToRefuseAStartOnAnyAvoidedCaption()
    {
        var args = JsonSerializer.SerializeToElement(
            SplitterPlan.DragArguments("w1", new Point(10, 640), new Point(10, 600), ["w1e7", "w1e9"])
        );

        Assert.Equal("drag", args.GetProperty("action").GetString());
        Assert.Equal("w1", args.GetProperty("handle").GetString());
        Assert.Equal(640, args.GetProperty("from").GetProperty("y").GetInt32());
        Assert.Equal(["w1e7", "w1e9"], args.GetProperty("fromMustNotHit").EnumerateArray().Select(e => e.GetString()));
    }
}
