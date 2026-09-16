using System;
using LlmServy.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LlmServy.Processes;

// Create suspended, attach to a kill-on-close Job, then resume: descendants cannot escape
// in the gap between Process.Start and AssignProcessToJobObject.
public sealed class OwnedProcess : IDisposable
{
    private const uint WaitTimeout = 258;
    private const uint KillOnJobClose = 0x2000;
    private const int ExtendedLimitInformation = 9;
    private const int UseStandardHandles = 0x100;
    private const uint CreateSuspendedWithoutWindow = 0x08000004;
    private static readonly TimeSpan BridgeShutdownTimeout = TimeSpan.FromSeconds(8);
    IntPtr job;
    IntPtr processHandle;
    StreamWriter? input;
    Task stdoutTask = Task.CompletedTask, stderrTask = Task.CompletedTask;
    public bool Running
    {
        get
        {
            if (processHandle == IntPtr.Zero)
                return false;
            var result = WaitForSingleObject(processHandle, 0);
            if (result == uint.MaxValue)
                throw new Win32Exception();
            return result == WaitTimeout;
        }
    }
    public int ExitCode
    {
        get
        {
            uint code;
            if (!GetExitCodeProcess(processHandle, out code))
                throw new Win32Exception();
            return (int)code;
        }
    }
    public static string Quote(string value)
    {
        // WSL parses switches itself: do not quote simple switches like -d or --exec.
        if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"' }) < 0)
            return value;
        var b = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            if (c == '"')
            {
                b.Append('\\', slashes * 2 + 1);
                b.Append(c);
            }
            else
            {
                b.Append('\\', slashes);
                b.Append(c);
            }
            slashes = 0;
        }
        b.Append('\\', slashes * 2);
        b.Append('"');
        return b.ToString();
    }
    /// <summary>Creates a suspended child, assigns its job, then resumes it. The caller owns the returned handle.</summary>
    public static OwnedProcess Start(string exe, string[] args, string cwd, Action<string, string> log)
    {
        var owned = new OwnedProcess();
        AnonymousPipeServerStream? output = null, error = null, stdin = null;
        PROCESS_INFORMATION pi = new PROCESS_INFORMATION();
        try
        {
            output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            error = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            stdin = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            owned.job = CreateJobObject(IntPtr.Zero, null);
            if (owned.job == IntPtr.Zero)
                throw new Win32Exception();
            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags = KillOnJobClose;
            int size = Marshal.SizeOf(limits);
            IntPtr data = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limits, data, false);
                if (!SetInformationJobObject(owned.job, ExtendedLimitInformation, data, (uint)size))
                    throw new Win32Exception();
            }
            finally { Marshal.FreeHGlobal(data); }
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.dwFlags = UseStandardHandles;
            si.hStdOutput = output.ClientSafePipeHandle.DangerousGetHandle();
            si.hStdError = error.ClientSafePipeHandle.DangerousGetHandle();
            si.hStdInput = stdin.ClientSafePipeHandle.DangerousGetHandle();
            var cmd = new StringBuilder(Quote(exe));
            foreach (string arg in args)
                cmd.Append(" ").Append(Quote(arg));
            if (!CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, true, CreateSuspendedWithoutWindow,
                IntPtr.Zero, cwd, ref si, out pi))
                throw new Win32Exception();
            if (!AssignProcessToJobObject(owned.job, pi.hProcess))
                throw new Win32Exception();
            output.DisposeLocalCopyOfClientHandle();
            error.DisposeLocalCopyOfClientHandle();
            stdin.DisposeLocalCopyOfClientHandle();
            owned.input = new StreamWriter(stdin, new UTF8Encoding(false));
            owned.input.AutoFlush = true;
            owned.stdoutTask = PumpAsync(output, "stdout", log);
            owned.stderrTask = PumpAsync(error, "stderr", log);
            if (ResumeThread(pi.hThread) == UInt32.MaxValue)
                throw new Win32Exception();
            owned.processHandle = pi.hProcess;
            pi.hProcess = IntPtr.Zero;
            return owned;
        }
        catch (Exception startupError)
        {
            try
            {
                ResourceCleanup.Run(() =>
                {
                    if (pi.hProcess != IntPtr.Zero && !TerminateProcess(pi.hProcess, 1))
                        throw new Win32Exception();
                }, owned.Dispose, () => output?.Dispose(), () => error?.Dispose(), () => stdin?.Dispose());
            }
            catch (Exception cleanupError) { throw new AggregateException(startupError, cleanupError); }
            throw;
        }
        finally
        {
            if (pi.hThread != IntPtr.Zero)
                CloseHandle(pi.hThread);
            if (pi.hProcess != IntPtr.Zero)
                CloseHandle(pi.hProcess);
        }
    }
    static Task PumpAsync(Stream stream, string channel, Action<string, string> log)
    {
        return Task.Run(() =>
        {
            try
            {
                using (var r = new StreamReader(stream, Encoding.UTF8))
                {
                    string? line;
                    while ((line = r.ReadLine()) != null)
                        log(channel, line);
                }
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                throw new AppException(new("ProcessOutputFailed", channel), error);
            }
        });
    }
    public void Send(string text)
    {
        if (input != null)
            input.WriteLine(text);
    }
    /// <summary>Waits for both output streams and reports read failures once.</summary>
    public async Task DrainAsync(CancellationToken token = default)
    {
        var output = Task.WhenAll(stdoutTask, stderrTask);
        try
        {
            await output.WaitAsync(token).ConfigureAwait(false);
        }
        catch (Exception) when (output.IsFaulted)
        {
            throw (Exception?)output.Exception ?? new InvalidOperationException("Output failed without an error.");
        }
        finally { if (output.IsCompleted) stdoutTask = stderrTask = Task.CompletedTask; }
    }
    public void CloseInput()
    {
        var writer = input;
        input = null;
        var pipe = writer?.BaseStream as PipeStream;
        try
        {
            writer?.Dispose();
        }
        catch (IOException error) when (IsClosedPipe(error, pipe))
        { /* The peer closed its input; EOF already reached it. */
        }
    }
    // PipeStream also reports an already broken pipe with a generic IOException.
    private static bool IsClosedPipe(IOException error, PipeStream? pipe) =>
        (error.HResult & 0xffff) is 109 or 232 || pipe is { IsConnected: false };

    /// <summary>Sends stop/EOF and waits up to eight seconds for the owned WSL supervisor.</summary>
    /// <exception cref="TimeoutException">The bridge remains alive; its handle is retained for retry.</exception>
    public async Task StopBridgeAsync()
    {
        try
        {
            Send("stop");
        }
        catch (IOException error) when (IsClosedPipe(error, input?.BaseStream as PipeStream))
        { /* Still wait for bridge exit below before confirming shutdown. */
        }
        CloseInput();
        var deadline = Stopwatch.StartNew();
        while (Running && deadline.Elapsed < BridgeShutdownTimeout)
            await Task.Delay(100).ConfigureAwait(false);
        if (Running)
            throw new TimeoutException("The WSL bridge did not acknowledge shutdown.");
        Dispose();
    }
    public void Dispose()
    {
        ResourceCleanup.Run(CloseInput, () =>
        {
            if (job == IntPtr.Zero)
                return;
            var handle = job;
            job = IntPtr.Zero;
            if (!CloseHandle(handle))
                throw new Win32Exception();
        }, () =>
        {
            if (processHandle == IntPtr.Zero)
                return;
            var handle = processHandle;
            processHandle = IntPtr.Zero;
            if (!CloseHandle(handle))
                throw new Win32Exception();
        });
    }
    [StructLayout(LayoutKind.Sequential)]
    struct STARTUPINFO
    {
        public int cb; public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BASIC_LIMIT
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit;
        public UIntPtr Affinity; public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS
    {
        public ulong a, b, c, d, e, f;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public BASIC_LIMIT BasicLimitInformation; public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr attrs, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int type, IntPtr info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
}
