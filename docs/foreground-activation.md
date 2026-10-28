# Foreground activation in unattended runs

`windows_launch` starts an allowed executable and attempts to attach one window owned by the new process. If it cannot identify a usable window, it returns `launch-pending` while the process is alive or `launch-handoff` when the launcher exits or returns no process handle. Neither receipt proves whether an app opened; inspect the window list and attach explicitly. A returned handle does not prove that the window is foreground or that a physical click can reach it. `windows_focus` restores a minimized target, calls `SetForegroundWindow`, then reports whether that exact native window became foreground. `GuardedInput` independently checks the registered process identity, foreground window, and native hit target before sending input.

[Microsoft's SetForegroundWindow rules](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow) restrict background applications from taking focus while another application is in use. Meeting the documented conditions still does not guarantee activation. A valid Process.Start launch does not make the broker foreground; Explorer or a terminal can remain foreground while `windows_launch` correctly returns a handle.

[AllowSetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-allowsetforegroundwindow) requires the granting process to be eligible to set the foreground window. A background broker cannot use it to grant itself that right. A cooperative foreground application could grant the broker or target process temporarily, but the grant may expire on the next user input.

## Recovery

1. Prepare an unlocked interactive desktop. Finish attended setup, dismiss menus, and clear competing windows and overlays before the unattended sequence.
2. Launch or explicitly attach, then check the returned handle and process identity. Request focus once and observe the actual foreground window before input.
3. If focus is denied, stop the input sequence. For an attended run, the person at the machine can click the intended window's title bar; re-observe its handle and process identity before continuing. For an unattended run, record the blocker and defer until the desktop is uncontested.
4. If focus succeeds but GetClickablePoint or the native hit check fails, inspect visible overlays and z-order. Clear an overlay through its owning app's normal lifecycle, then re-observe. Do not click through it or disable the guard.

Foreground authorization and pixel occlusion are separate conditions. The current activation-denied message means Windows retained another foreground window; an obscured clickable point means the intended target was not the native hit window.

This workflow excludes synthetic Alt presses, shell clicks, thread-input attachment, foreground-lock changes, and privilege elevation. They change desktop state without establishing that the intended target owns the next input.

Cold launches can take longer than the default 20-second window detection period. For `windows_launch`, use `waitSeconds` (1–90) when the app is known to start slowly. The server grants that tool `waitSeconds + 15` seconds (35 seconds by default) for input-idle and provider reads; set the MCP caller timeout above that deadline. The demo broker's default 120 seconds covers the maximum 105-second launch. The longer wait does not relax same-process ownership or foreground checks. If the process remains alive without one usable window, the reply is `launch-pending` with its PID and observed window titles/HWNDs; if it exits or returns no process handle, the reply is `launch-handoff`. Inspect or attach rather than relaunching it.
