// SPDX-FileCopyrightText: 2026 Dennis40816
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.IO.Pipes;
using DSView.Checks.Support;
using DSView.Infrastructure;

internal static class NativeWorkerJobChecks
{
    public static async Task RunProbeAsync(string[] args)
    {
        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(); await using var writer = new StreamWriter(pipe) { AutoFlush = true };
        await writer.WriteLineAsync(Environment.ProcessId.ToString());
        if (args[3] == "stdout") { for (int i = 0; i < 1100; i++) Console.Write(new string('X', 4096)); }
        if (args[3] == "stderr") { for (int i = 0; i < 20; i++) Console.Error.Write(new string('E', 4096)); }
        if (args[3] == "exit") { Console.Error.Write("Controlled worker failure"); Environment.ExitCode = 7; return; }
        await Task.Delay(Timeout.Infinite); // Parent must terminate this confirmed-live child.
    }
    public static async Task RunAsync(Action<bool, string> check)
    {
        foreach (string behavior in new[] { "cancel", "stdout", "stderr", "exit" })
        {
            using var files = new TestWorkspace(); files.Write("keep.txt", "Unrelated fixture data");
            string pipeName = "dsview2-check-" + Guid.NewGuid().ToString("N");
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            using var cancel = new CancellationTokenSource(); string? input = null;
            Task<string> job = NativeWorkerJob.RunAsync([0x12, 0x34], path =>
            {
                input = path; var start = new ProcessStartInfo("dotnet");
                foreach (string argument in new[] { typeof(NativeWorkerJobChecks).Assembly.Location, "--worker-lifetime-probe", pipeName, path, behavior }) start.ArgumentList.Add(argument);
                return start;
            }, cancel.Token);
            try
            {
                await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(15));
                using var reader = new StreamReader(pipe); int pid = int.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)))!);
                if (behavior == "cancel")
                {
                    using var child = Process.GetProcessById(pid);
                    if (child.HasExited || !File.Exists(input)) throw new Exception("Cancellation must target a live child with owned input.");
                    cancel.Cancel();
                }
                bool expected = false;
                try { await job.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (OperationCanceledException) when (behavior == "cancel") { expected = true; }
                catch (InvalidDataException ex) when (behavior != "cancel") { expected = behavior == "exit" ? ex.Message.Contains("Controlled worker failure", StringComparison.Ordinal) : ex.Message.Contains("exceeded", StringComparison.Ordinal); }
                bool exited;
                try { using var child = Process.GetProcessById(pid); exited = child.HasExited; } catch (ArgumentException) { exited = true; }
                check(expected && exited && input != null && !Directory.Exists(Path.GetDirectoryName(input)) && files.Read("keep.txt") == "Unrelated fixture data",
                    $"Native worker {behavior}: confirms child exit, cleans owned input, preserves unrelated data and reports the original failure");
            }
            finally { cancel.Cancel(); try { await job; } catch { } }
        }
        string? failedInput = null; bool startFailed = false;
        try
        {
            await NativeWorkerJob.RunAsync([1], input => { failedInput = input; return new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(input)!, "absent-worker.exe")); }, CancellationToken.None);
        }
        catch (System.ComponentModel.Win32Exception) { startFailed = true; }
        check(startFailed && failedInput != null && !Directory.Exists(Path.GetDirectoryName(failedInput)), "Worker startup failure removes its generated input without touching other paths");
    }
}
