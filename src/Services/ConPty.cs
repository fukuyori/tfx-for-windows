using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Tfx;

/// <summary>
/// Thin wrapper around the Windows Pseudo Console (ConPTY) API. Spawns a child
/// shell attached to a pseudo console, exposes its output as a byte stream
/// (raw VT — the terminal control interprets the escapes), and lets the caller
/// write input and resize the console.
///
/// Lifecycle: construct → <see cref="Start"/> → read <see cref="OutputReceived"/>
/// / call <see cref="Write"/> / <see cref="Resize"/> → <see cref="Dispose"/>.
/// All native handles are released on dispose; the child process is terminated
/// when the pseudo console closes.
/// </summary>
internal sealed class ConPty : IDisposable
{
    private IntPtr _hpc = IntPtr.Zero;             // HPCON
    private SafeFileHandle? _inputWrite;           // we write -> child stdin
    private SafeFileHandle? _outputRead;           // child stdout -> we read
    private SafeFileHandle? _inputReadChild;       // child end of stdin
    private SafeFileHandle? _outputWriteChild;     // child end of stdout
    private Process? _process;
    private FileStream? _writer;
    private Thread? _readThread;
    private volatile bool _disposed;

    /// <summary>Raised on a background thread with raw bytes read from the child.</summary>
    public event Action<byte[]>? OutputReceived;

    /// <summary>Raised (background thread) when the child process exits / the pipe closes.</summary>
    public event Action? Exited;

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// Starts <paramref name="commandLine"/> in a new pseudo console of the
    /// given size, with <paramref name="workingDirectory"/> as its cwd.
    /// </summary>
    public void Start(string commandLine, string workingDirectory, short columns, short rows)
    {
        if (columns < 1) columns = 80;
        if (rows < 1) rows = 25;

        // Two anonymous pipes: one for input (we -> child), one for output.
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0))
        {
            throw new InvalidOperationException("CreatePipe(input) failed.");
        }
        if (!CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0))
        {
            inputRead.Dispose();
            inputWrite.Dispose();
            throw new InvalidOperationException("CreatePipe(output) failed.");
        }

        _inputReadChild = inputRead;
        _inputWrite = inputWrite;
        _outputRead = outputRead;
        _outputWriteChild = outputWrite;

        var size = new COORD { X = columns, Y = rows };
        var hr = CreatePseudoConsole(size, inputRead, outputWrite, 0, out _hpc);
        if (hr != 0)
        {
            throw new InvalidOperationException($"CreatePseudoConsole failed (0x{hr:X8}).");
        }

        StartChild(commandLine, workingDirectory);

        _writer = new FileStream(_inputWrite, FileAccess.Write);
        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "ConPty-read" };
        _readThread.Start();
    }

    private void StartChild(string commandLine, string workingDirectory)
    {
        var attrListSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);
        var attrList = Marshal.AllocHGlobal(attrListSize);
        try
        {
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref attrListSize))
            {
                throw new InvalidOperationException("InitializeProcThreadAttributeList failed.");
            }
            if (!UpdateProcThreadAttribute(attrList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    _hpc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            {
                throw new InvalidOperationException("UpdateProcThreadAttribute failed.");
            }

            var startupInfo = new STARTUPINFOEX();
            startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startupInfo.lpAttributeList = attrList;

            var securityAttributes = new SECURITY_ATTRIBUTES();
            securityAttributes.nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>();

            var wd = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory;
            if (!CreateProcess(
                    null,
                    new System.Text.StringBuilder(commandLine),
                    ref securityAttributes,
                    ref securityAttributes,
                    false,
                    EXTENDED_STARTUPINFO_PRESENT,
                    IntPtr.Zero,
                    wd,
                    ref startupInfo,
                    out var procInfo))
            {
                throw new InvalidOperationException($"CreateProcess failed ({Marshal.GetLastWin32Error()}).");
            }

            _process = Process.GetProcessById(procInfo.dwProcessId);
            // The ConPTY host (conhost) keeps the output pipe's write end open
            // for its own lifetime, so the read loop never sees EOF when the
            // shell exits (e.g. the user types `exit`). Watch the child process
            // directly and raise Exited when it terminates.
            try
            {
                _process.EnableRaisingEvents = true;
                _process.Exited += (_, _) => RaiseExited();
            }
            catch
            {
                // If event wiring fails the read-loop EOF path is the fallback.
            }
            // We don't need the child's primary thread/process handles from
            // CreateProcess; close them so only the pseudo console keeps the
            // child alive.
            CloseHandle(procInfo.hThread);
            CloseHandle(procInfo.hProcess);

            // The child now owns its ends of the pipes; close ours so EOF
            // propagates correctly when the child exits.
            _inputReadChild?.Dispose();
            _inputReadChild = null;
            _outputWriteChild?.Dispose();
            _outputWriteChild = null;
        }
        finally
        {
            DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal(attrList);
        }
    }

    private void ReadLoop()
    {
        try
        {
            using var reader = new FileStream(_outputRead!, FileAccess.Read);
            var buffer = new byte[4096];
            while (!_disposed)
            {
                int read;
                try
                {
                    read = reader.Read(buffer, 0, buffer.Length);
                }
                catch
                {
                    break;
                }
                if (read <= 0)
                {
                    break;
                }
                var chunk = new byte[read];
                Array.Copy(buffer, chunk, read);
                OutputReceived?.Invoke(chunk);
            }
        }
        catch
        {
            // Pipe closed / process gone — fall through to Exited.
        }
        finally
        {
            RaiseExited();
        }
    }

    private int _exitedRaised;

    /// <summary>
    /// Fires <see cref="Exited"/> exactly once, whether triggered by the
    /// process-exit event or the read-loop EOF. Suppressed during Dispose.
    /// </summary>
    private void RaiseExited()
    {
        if (_disposed)
        {
            return;
        }
        if (Interlocked.Exchange(ref _exitedRaised, 1) == 0)
        {
            Exited?.Invoke();
        }
    }

    /// <summary>Writes UTF-8 text to the child's stdin.</summary>
    public void Write(string text) => WriteBytes(System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>Writes raw bytes to the child's stdin (input from the terminal UI).</summary>
    public void WriteBytes(byte[] bytes)
    {
        if (_writer is null || _disposed || bytes.Length == 0)
        {
            return;
        }
        try
        {
            _writer.Write(bytes, 0, bytes.Length);
            _writer.Flush();
        }
        catch
        {
            // Child likely exited; ignore.
        }
    }

    /// <summary>Resizes the pseudo console grid (called when the terminal view is resized).</summary>
    public void Resize(short columns, short rows)
    {
        if (_hpc == IntPtr.Zero || _disposed)
        {
            return;
        }
        if (columns < 1) columns = 1;
        if (rows < 1) rows = 1;
        ResizePseudoConsole(_hpc, new COORD { X = columns, Y = rows });
    }

    /// <summary>
    /// Closes the pseudo console (which signals the child to exit), then releases
    /// pipes and kills the child process tree if it is still alive. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Closing the pseudo console signals the child to exit.
        if (_hpc != IntPtr.Zero)
        {
            ClosePseudoConsole(_hpc);
            _hpc = IntPtr.Zero;
        }

        try { _writer?.Dispose(); } catch { }
        try { _outputRead?.Dispose(); } catch { }
        try { _inputWrite?.Dispose(); } catch { }
        try { _inputReadChild?.Dispose(); } catch { }
        try { _outputWriteChild?.Dispose(); } catch { }

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch { }
        _process?.Dispose();
    }

    // ─── Win32 ────────────────────────────────────────────────────────────

    private const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = (IntPtr)0x00020016;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        System.Text.StringBuilder lpCommandLine,
        ref SECURITY_ATTRIBUTES lpProcessAttributes,
        ref SECURITY_ATTRIBUTES lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
