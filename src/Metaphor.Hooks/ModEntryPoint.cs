using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Metaphor.Core;
using Metaphor.Core.Announcements;
using Metaphor.Core.Cues;
using Metaphor.Core.Menus;
using Metaphor.Core.Settings;
using Metaphor.Features.Menus;
using Metaphor.Features.Updates;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;

namespace Metaphor.Hooks;

/// <summary>The Reloaded-II entry point: the loader starts the first <see cref="IModV1"/> type in the mod's DLL
/// (docs/raw-sources/reloaded-ii-mod-entry-point-2026-11-15.md). A thin adapter: the work is in
/// <see cref="ModStartup"/>, and nothing here may throw into the game.</summary>
public sealed class ModEntryPoint : IMod
{
    /// <summary>Starts the announcer every feature speaks through (ADR 0004): Prism when it starts, silence otherwise,
    /// and in Debug builds the transcript in front of either.</summary>
    private static IAnnouncer StartSpeech(string folder, Action<string> log)
    {
        var (speech, whyNot) = PrismSpeech.TryStart(folder, log);
        log(speech is null ? $"speech unavailable: {whyNot}"
            : speech.BackendName is { } name ? $"speech: {name}"
            : SwitchingAnnouncer.NoBackend);
        IAnnouncer announcer = speech is null ? new SilentAnnouncer() : speech;
#if DEBUG
        announcer = new TranscriptAnnouncer(announcer, folder);
#endif
        return announcer;
    }

    /// <summary>The report log a player attaches to a bug report (#188), fresh each run, in every build, redacting what
    /// <see cref="ReportLog.PathsToRedact"/> lists. Null when it can't be opened.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A report log must never stop the mod.")]
    [SuppressMessage("Reliability", "CA2000", Justification = "Lives as long as the game process.")]
    private static ReportLog? OpenReportLog(string folder, string configFolder)
    {
        try
        {
            var stream = new FileStream(
                Path.Combine(folder, ReportLog.FileName), FileMode.Create, FileAccess.Write, FileShare.Read);
            var started = Process.GetCurrentProcess().StartTime;
            var report = new ReportLog(
                new StreamWriter(stream), () => (long)(DateTime.Now - started).TotalMilliseconds,
                ReportLog.PathsToRedact(
                    folder, configFolder, Environment.ProcessPath,
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName));
            // The queue's last lines are the ones a crash report needs, so they're written on the way out, waiting
            // briefly so exit can't hang (#204). A crash in native code runs neither handler.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => DrainBeforeExit(report);
            AppDomain.CurrentDomain.UnhandledException += (_, _) => DrainBeforeExit(report);
            return report;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "An exit handler must never throw.")]
    private static void DrainBeforeExit(ReportLog report)
    {
        try
        {
            report.WaitForWrites(ReportLog.ErrorWriteTimeout);
        }
        catch (Exception)
        {
            // Exiting anyway; nothing to report to.
        }
    }

#if DEBUG
    /// <summary>#55's message log, fresh each run, readable while the game writes it. Null when it can't be opened.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A spike log must never stop the mod.")]
    [SuppressMessage("Reliability", "CA2000", Justification = "Lives as long as the game process.")]
    private static MessageLog? OpenMessageLog(string folder)
    {
        try
        {
            var stream = new FileStream(
                Path.Combine(folder, MessageLog.FileName), FileMode.Create, FileAccess.Write, FileShare.Read);
            var started = System.Diagnostics.Process.GetCurrentProcess().StartTime;
            return new MessageLog(new StreamWriter(stream), () => (long)(DateTime.Now - started).TotalMilliseconds);
        }
        catch (Exception)
        {
            return null;
        }
    }
#endif

    // Kept so the watcher lives as long as the game process.
    private SettingsFile? settingsFile;
    // The sound cue player and its background thread, for the same reason; never disposed, like the watcher.
    private ICuePlayer? cues;
#if DEBUG
    // The live inspector (#291), Debug and opt-in only; kept for the same reason.
    private LiveInspector? liveInspector;
#endif

    public Action Disposing => () => { };

    [SuppressMessage("Design", "CA1031", Justification = "An exception here would reach the game through the loader.")]
    public void StartEx(IModLoaderV1 loader, IModConfigV1 config)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(loader);
            ArgumentNullException.ThrowIfNull(config);
            var modLoader = (IModLoader)loader;
            var folder = modLoader.GetDirectoryForModId(config.ModId);
            var logger = (ILogger)modLoader.GetLogger();
            // Every line for the loader's console also goes to the report log (#188).
            // The settings live in Reloaded-II's per-mod user-config folder (IModLoaderV3.GetModConfigDirectory, in
            // Reloaded.Mod.Interfaces 2.4.0), read now and again when the file changes (#104).
            var configFolder = modLoader.GetModConfigDirectory(config.ModId);
            var report = OpenReportLog(folder, configFolder);
            void Log(string line)
            {
                logger.WriteLine($"[{config.ModId}] {line}");
                report?.Note(line);
            }
            // An error is on disk before the game goes on, in case a crash follows (#204).
            void LogError(string line)
            {
                logger.WriteLine($"[{config.ModId}] {line}");
                report?.Error(line);
            }
            // Speech and the logs first: hooks are created with them, and one may fire at once.
            var announcer = StartSpeech(folder, Log);
#if DEBUG
            var messageLog = OpenMessageLog(folder);
#else
            MessageLog? messageLog = null;
#endif
            // The settings report only a file they can't use, so each of their lines is an error.
            var settings = new SettingsReloader(announcer, LogError);
            settingsFile = SettingsFile.Open(configFolder, settings, LogError);
            // Sound cues play on their own thread through waveOut channels, at the settings in force (#636, #637,
            // ADR 0019).
            cues = new CuePlayer(new WaveOutDevice(), () => settings.Current, LogError);
            var services = HookServices.Speaking(announcer, cues, settings, report, messageLog, folder);
            // Before the hooks, so the title screen's first events reach it; its outcome is logged in Debug builds only.
#if DEBUG
            Action<string> updateLog = Log;
#else
            Action<string> updateLog = _ => { };
#endif
            StartUpdateCheck(config.ModVersion ?? "", announcer, settings, services.MenuScreens, updateLog);
            // The mod runs inside the game, so this process's exe is the game executable whose hash identifies the
            // build (seen in docs/raw-sources/mod-entry-point-first-run-2026-11-15.md). Hooks go in only on a known
            // build, through the loader (#39). Issue #21's spike scans first, so it reads the game's own bytes (#92).
            var candidates = HookLoader.Candidates(folder);
            var status = ModStartup.CheckAndReport(
                config.ModId, Environment.ProcessPath ?? "", folder, () => KnownGameBuilds.All, HookLoader.Expected(candidates),
                build => HookLoader.Install(
                    build, candidates, modLoader, services, Log, LogError),
                build => ScanIfRequested(folder, build, Log, LogError),
                announcer);
            // The load message is spoken (#187); the log repeats it for when speech didn't start. A reason means
            // something went wrong, so it's written as an error.
            (status.Reason is null ? (Action<string>)Log : LogError)(
                $"build {ModBuild.Revision.CommitSha}{(ModBuild.Revision.HasUncommittedChanges ? "-dirty" : "")}: {status.State} on game build {status.GameBuildSha256}, hooks {status.HooksLoaded}/{status.HooksExpected}" +
                (status.Reason is null ? "" : $" ({status.Reason})") + $"; load message: {LoadMessage.For(status).Text}");
            // The tutorial table is the game's own data, read from its archive beside the exe (#118) once hooks are in
            // on a known build; off the game's thread, and never written.
            if (status.HooksLoaded > 0 && Path.GetDirectoryName(Environment.ProcessPath) is { Length: > 0 } gameFolder)
            {
                _ = services.Tutorials.LoadInBackground(() => File.OpenRead(Path.Combine(gameFolder, "base.cpk")), Log);
            }
#if DEBUG
            // After the hooks, so its reads never race their installation; read-only, so the build check isn't needed.
            liveInspector = LiveInspector.StartIfOptedIn(folder, Log, LogError);
#endif
        }
        catch (Exception)
        {
            // ModStartup doesn't throw; this covers the loader API, whose failures we can't report anywhere.
        }
    }

    /// <summary>The update notice (#576, ADR 0018): it hears the menu screens, and one request for this repository's
    /// releases goes out on a pool thread, unless the settings file turns the check off. The request has a short
    /// timeout and fails silently.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A failed update check must never reach the game.")]
    private static void StartUpdateCheck(
        string modVersion, IAnnouncer announcer, SettingsReloader settings, MenuScreens screens, Action<string> log)
    {
        var notice = new UpdateNotice(
            modVersion, TitleMenuFocus.Screen, announcer, () => settings.Current.CheckForUpdates, log);
        screens.Observe(notice);
        if (!settings.Current.CheckForUpdates)
        {
            log("update check: off in the settings");
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                using var releases = GitHubReleases.Create();
                await notice.CheckAsync(releases, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                log($"update check failed: {e.GetType().Name}");
            }
        });
    }

    /// <summary>Issue #21's spike: runs once, only when scripts/dev/session.ps1 spike put a probes file in the mod's
    /// folder, and before any hook installs. Its result goes through <paramref name="log"/>, or
    /// <paramref name="logError"/> when it failed, so the report log has it too (#204).</summary>
    private static void ScanIfRequested(string folder, string build, Action<string> log, Action<string> logError)
    {
        var scan = SignatureScan.ScanIfRequested(folder, build, OpenMainModuleForScan);
        if (scan is null)
        {
            return;
        }

        if (scan.Error is null)
        {
            log($"signature scan: {scan.Probes.Count(probe => probe.Matches == 1)} of {scan.Probes.Count} probes matched once; {scan.ReadableBytes} of {scan.ModuleBytes} bytes read, {scan.UnreadableBytes} failed; {scan.MillisecondsSinceGameStart} ms after start ({SignatureScan.ReportFileName})");
        }
        else
        {
            logError($"signature scan failed: {scan.Error}");
        }
    }

    /// <summary>The game's main module for the signature scan, read a chunk at a time through
    /// <see cref="ModuleMemoryReader"/> as the hook loader reads it.</summary>
    private static ScannedModule OpenMainModuleForScan()
    {
        using var process = Process.GetCurrentProcess();
        var sinceStart = (long)(DateTime.Now - process.StartTime).TotalMilliseconds;
        var moduleBytes = process.MainModule?.ModuleMemorySize ??
            throw new InvalidOperationException("the game process has no main module");
        var (moduleBase, regions) = ModuleMemoryReader.MainModuleRegions();
        return new(moduleBytes, sinceStart, regions, (rva, buffer) => ModuleMemoryReader.ReadAt(moduleBase, rva, buffer));
    }

    // No hooks are installed yet, so there is nothing to suspend or unload; false hides those launcher buttons.
    public void Suspend() { }

    public void Resume() { }

    public void Unload() { }

    public bool CanUnload() => false;

    public bool CanSuspend() => false;
}
