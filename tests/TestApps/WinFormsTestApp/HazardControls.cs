using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinFormsTestApp;

/// <summary>
/// A focusable box that takes typed keys and exposes no Value or Text pattern, so a fill can only reach it by
/// keyboard. Ctrl+A selects its content; the next character or Backspace replaces it.
/// </summary>
internal sealed class KeysOnlyBox : Control
{
    private bool allSelected;

    public KeysOnlyBox()
    {
        SetStyle(
            ControlStyles.Selectable
                | ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer,
            true
        );
        TabStop = true;
        BackColor = Color.White;
    }

    public event EventHandler? ContentChanged;

    public string Content { get; private set; } = "";

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        switch (e.KeyChar)
        {
            case '\x01':
                allSelected = true;
                break;
            case '\b':
                Content = allSelected || Content.Length == 0 ? "" : Content[..^1];
                allSelected = false;
                break;
            default:
                if (!char.IsControl(e.KeyChar))
                {
                    Content = (allSelected ? "" : Content) + e.KeyChar;
                    allSelected = false;
                }

                break;
        }

        e.Handled = true;
        ContentChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
        base.OnKeyPress(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        TextRenderer.DrawText(e.Graphics, Content, Font, ClientRectangle, ForeColor, TextFormatFlags.VerticalCenter);
        ControlPaint.DrawBorder(
            e.Graphics,
            ClientRectangle,
            Focused ? Color.DodgerBlue : Color.Gray,
            ButtonBorderStyle.Solid
        );
        base.OnPaint(e);
    }
}

/// <summary>
/// A pane with no Value pattern that will not keep keyboard focus: it hands focus straight back to the window that
/// had it. A plain Panel is not enough, since Win32 SetFocus (which UI Automation uses) focuses any window.
/// </summary>
internal sealed class FocusRefusingPane : Panel
{
    private const int WmSetFocus = 0x0007;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmSetFocus && m.WParam != IntPtr.Zero)
        {
            SetFocus(m.WParam);
            return;
        }

        base.WndProc(ref m);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);
}

/// <summary>
/// A borderless window over the whole virtual screen, every monitor included, so there is no point off it to
/// park the pointer on. It is created per-monitor aware so its bounds are physical pixels whatever the app's own
/// DPI mode; Escape closes it.
/// </summary>
internal static class FullScreenWindow
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint SwpNoZOrder = 0x0004;
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    public static void Open()
    {
        var previous = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        try
        {
            var form = new Form
            {
                Text = "Full Screen Window",
                Name = "FullScreenWindow",
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                BackColor = Color.DarkSlateGray,
                KeyPreview = true,
            };
            form.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    form.Close();
                }
            };
            form.Show();
            SetWindowPos(
                form.Handle,
                IntPtr.Zero,
                GetSystemMetrics(SmXVirtualScreen),
                GetSystemMetrics(SmYVirtualScreen),
                GetSystemMetrics(SmCxVirtualScreen),
                GetSystemMetrics(SmCyVirtualScreen),
                SwpNoZOrder
            );
        }
        finally
        {
            SetThreadDpiAwarenessContext(previous);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
