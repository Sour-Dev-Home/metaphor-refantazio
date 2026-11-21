# The players' PowerShell module (#412): everything install.ps1 and check-install.ps1 run, and the reading and locating
# the developer scripts share (scripts/dev/ReloadedDev.psm1 imports it). It ships in the release beside the mod, so it
# must load in Windows PowerShell 5.1: ASCII only. Its sections, in order:
# - Reloaded-II reading: reads files only; nothing writes, starts a process or reaches the network.
# - Questions that are safe without a console.
# - Finding the game and Reloaded-II.
# - The install check (check-install.ps1).
# - The installer's steps (install.ps1).
# Only the functions at the end (Export-ModuleMember) are its interface; the rest are its own.

Set-StrictMode -Version Latest

# --- Reloaded-II reading ----------------------------------------------------------------------------------------------
# Read-only Reloaded-II helpers. Loader facts: docs/raw-sources/reloaded-ii-config-location-2026-11-13.md and
# reloaded-ii-app-and-mod-config-2026-11-15.md.

$StatusFileName = 'mod-status.json' # Metaphor.Core.ModStatusFile.FileName

# Dates stay strings, so a config written back keeps them as the loader wrote them. Windows PowerShell 5.1 has no
# -DateKind and leaves dates as strings anyway. An array comes back one item at a time in both.
# Reloaded-II writes UTF-8 without a byte order mark, which 5.1 reads as the ANSI code page unless told UTF8 (#453);
# UTF8 reads a file with a byte order mark too.
$JsonKeepsDates = (Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')
function Read-Json([string] $Path) {
    $text = Get-Content -Raw -Encoding UTF8 -LiteralPath $Path
    if ($JsonKeepsDates) { $text | ConvertFrom-Json -DateKind String } else { foreach ($item in ($text | ConvertFrom-Json)) { $item } }
}

# Reloaded-II's root config, at a fixed AppData path the loader doesn't let us move
# (reloaded-ii-config-location-2026-11-13.md). Scripts that take it as a parameter default spell it out, since a
# default is evaluated before the module loads.
function Get-ReloadedConfigPath {
    Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Reloaded-Mod-Loader-II/ReloadedII.json'
}

# As Reloaded-II's ApplicationConfig.GetAbsoluteAppLocation: a relative AppLocation is relative to its AppConfig.json.
function Resolve-AppLocation([string] $ConfigFile, [string] $Location) {
    $folder = Split-Path $ConfigFile
    if (-not [IO.Path]::IsPathRooted($Location)) { $Location = Join-Path $folder $Location }
    elseif ([IO.Path]::GetPathRoot($Location) -eq '\') { $Location = Join-Path ([IO.Path]::GetPathRoot($folder)) $Location.TrimStart('\') }
    [IO.Path]::GetFullPath($Location)
}

# True when reading PATH could go over the network: a UNC path (\\host\share, or \\?\UNC\...) or a mapped network
# drive. Decided from the text and the drive's type, without touching the path itself.
function Test-NetworkPath([string] $Path) {
    if (-not $Path) { return $false }
    if ($Path -match '^[\\/]{2}') { return $true }
    if ($Path -match '^([A-Za-z]):') {
        try { return ([IO.DriveInfo]::new($Matches[1])).DriveType -eq [IO.DriveType]::Network } catch { return $false }
    }
    return $false
}

# A file's SHA-256 in lowercase hex. Through .NET rather than Get-FileHash: Windows PowerShell 5.1 started from
# PowerShell 7 inherits 7's module path and can't load Get-FileHash's module
# (docs/raw-sources/install-check-sources-2026-11-19.md).
function Get-Sha256([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $hash = [Security.Cryptography.SHA256]::Create()
    try { -join ($hash.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) }
    finally { $hash.Dispose(); $stream.Dispose() }
}

# The game exe's SHA-256, and its entry in the known-builds list ($null when it isn't listed).
function Get-GameBuild([string] $GameExe, [string] $KnownBuilds) {
    $sha256 = Get-Sha256 $GameExe
    [pscustomobject]@{ Sha256 = $sha256; Known = @(Read-Json $KnownBuilds) | Where-Object sha256 -EQ $sha256 }
}

# The AppConfig.json file of the application registered as metaphor.exe, the AppId Metaphor mods target
# (reloaded-ii-mod-entry-point-2026-11-15.md); $null when none is.
function Find-MetaphorApplication($Loader) {
    Get-ChildItem -LiteralPath $Loader.ApplicationConfigDirectory -Filter AppConfig.json -Recurse -Depth 1 |
        Where-Object { (Read-Json $_.FullName).AppId -eq 'metaphor.exe' } | Select-Object -First 1
}

# The launcher and game exe to use: those given, or else Reloaded-II's own records of them. The launcher from
# ReloadedII.json's LauncherPath (rewritten on each launch: reloaded-ii-config-location-2026-11-13.md), the game from the
# application registered as metaphor.exe (the AppId Metaphor mods target: reloaded-ii-mod-entry-point-2026-11-15.md).
function Resolve-LoaderPaths($Loader, [string] $GameExe, [string] $LauncherExe) {
    if (-not $LauncherExe -and $Loader.PSObject.Properties['LauncherPath']) { $LauncherExe = $Loader.LauncherPath }
    if (-not $GameExe) {
        $game = Find-MetaphorApplication $Loader
        if ($game) { $GameExe = Resolve-AppLocation $game.FullName (Read-Json $game.FullName).AppLocation }
    }
    [pscustomobject]@{ GameExe = $GameExe; LauncherExe = $LauncherExe }
}

# --- Questions that are safe without a console -----------------------------------------------------------------------
# Questions that are safe without a console (#267), shared by install.ps1, onboard.ps1 and setup-wsl.ps1. Under pwsh
# -NonInteractive, Read-Host throws; with input redirected or no user session, it can wait forever. Without a console
# these functions say so and take the answer as no instead.

# True when someone can answer: a user session, input from a console, and no -NonInteractive on the command line.
function Test-Interactive {
    if (-not [Environment]::UserInteractive) { return $false }
    if ([Console]::IsInputRedirected) { return $false }
    if (@([Environment]::GetCommandLineArgs() | Where-Object { $_ -match '^-noni' }).Count -gt 0) { return $false }
    return $true
}

# Shows QUESTION and returns the line typed ('' for Enter), or $null without a console.
function Read-Answer([string] $Question) {
    Write-Host "$Question " -NoNewline
    if (-not (Test-Interactive)) {
        Write-Host '(no console to answer on, so no)'
        return $null
    }
    $answer = Read-Host
    if ($null -eq $answer) { return '' }
    return $answer
}

# QUESTION followed by the one sentence that says what to type, for every yes/no question the installer and setup
# scripts ask (#473): a screen reader reads [y/N] as letters. Enter alone means no, or yes with ENTERMEANSYES. ENDING
# says what no does: 'stop' where the script then stops, 'skip it' where it carries on without that item.
function Format-YesNoQuestion([string] $Question, [switch] $EnterMeansYes,
    [ValidateSet('stop', 'skip it')] [string] $Ending = 'stop') {
    $how = if ($EnterMeansYes) { "Press Enter alone to go on, or type no and press Enter to $Ending." }
    else { "Type yes and press Enter to go on, or press Enter alone to $Ending." }
    "$Question $how"
}

# True for y or yes; Enter alone ('') and no answer ($null) are no.
function Test-Yes($Answer) { [bool]($null -ne $Answer -and $Answer -match '^\s*(y|yes)\s*$') }

# True for Enter alone, y or yes: the answer to a question asked with Format-YesNoQuestion -EnterMeansYes.
function Test-YesOrEnter($Answer) { [bool]($null -ne $Answer -and $Answer -match '^\s*(y|yes)?\s*$') }

# True when the answer to QUESTION is y or yes; no without a console.
function Confirm-Step([string] $Question) { Test-Yes (Read-Answer (Format-YesNoQuestion $Question)) }

# --- The installer's questions ---------------------------------------------------------------------------------------
# install.ps1 asks each yes/no question in a Windows message box with Yes and No buttons (#607): the screen reader reads
# its text and buttons itself, so the question isn't also spoken through it, and no one types an answer. The box is
# owned by the console window and asks to come to the front, so the focus lands on it. Its default button is the answer
# Enter gave when the question was typed. A box with Yes and No and no Cancel button ignores Escape, by Windows'
# MessageBox reference [NEEDS VERIFICATION] by ear (#573); N answers No. The game's file, when Steam has none, is
# chosen in Windows' Open dialog. A scripted run (-Answers), or one without a console window or a desktop session, asks
# in the console as before; so does a question whose window doesn't open.

# The title of every question's window.
$QuestionCaption = 'Metaphor ReFantazio accessibility mod'
# MessageBoxW's flags and results: MB_YESNO, MB_ICONQUESTION, MB_DEFBUTTON2, MB_SETFOREGROUND; IDYES, IDNO.
$MessageBoxFlag = @{ YesNo = 0x4; Question = 0x20; SecondDefault = 0x100; SetForeground = 0x10000 }
$MessageBoxResult = @{ Yes = 6; No = 7 }

# A type with user32's MessageBoxW and kernel32's GetConsoleWindow, built in memory as Get-FindWindow is (#459).
$DialogType = $null
function Get-DialogNative {
    if (-not $script:DialogType) {
        $access = [Reflection.Emit.AssemblyBuilderAccess]::Run
        $assembly = [Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly((New-Object Reflection.AssemblyName 'MetaphorInstallDialog'), $access)
        $type = $assembly.DefineDynamicModule('MetaphorInstallDialog').DefineType('MetaphorInstallDialog.Native',
            [Reflection.TypeAttributes] 'Public, Abstract, Sealed')
        $calls = @(
            @('user32.dll', 'MessageBoxW', [Int32], @([IntPtr], [string], [string], [UInt32])),
            @('kernel32.dll', 'GetConsoleWindow', [IntPtr], @()))
        foreach ($call in $calls) {
            $method = $type.DefinePInvokeMethod($call[1], $call[0], $call[1], [Reflection.MethodAttributes] 'Public, Static, PinvokeImpl',
                [Reflection.CallingConventions]::Standard, $call[2], [Type[]] $call[3],
                [Runtime.InteropServices.CallingConvention]::Winapi, [Runtime.InteropServices.CharSet]::Unicode)
            $method.SetImplementationFlags([Reflection.MethodImplAttributes]::PreserveSig)
        }
        $script:DialogType = $type.CreateType()
    }
    $script:DialogType
}

# True when the questions can open windows: someone can answer (Test-Interactive) and there is a console window to own
# them.
function Test-QuestionWindow {
    if (-not (Test-Interactive)) { return $false }
    try { (Get-DialogNative)::GetConsoleWindow() -ne [IntPtr]::Zero } catch { $false }
}

# The button pressed in a message box owned by the console window: $MessageBoxResult's, or 0 when it didn't open.
function Show-MessageBox([string] $Text, [string] $Caption, [UInt32] $Flags) {
    $native = Get-DialogNative
    $native::MessageBoxW($native::GetConsoleWindow(), $Text, $Caption, $Flags)
}

# A type that implements WinForms' IWin32Window with a handle given to its constructor and nothing else, built in memory
# as Get-FindWindow is (#459). It only reports the console window as a dialog's owner: NativeWindow.AssignHandle would
# subclass that window, which belongs to the console host's process, not this one.
$OwnerType = $null
function Get-OwnerType {
    if (-not $script:OwnerType) {
        Add-Type -AssemblyName System.Windows.Forms
        $access = [Reflection.Emit.AssemblyBuilderAccess]::Run
        $assembly = [Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly((New-Object Reflection.AssemblyName 'MetaphorInstallOwner'), $access)
        $type = $assembly.DefineDynamicModule('MetaphorInstallOwner').DefineType('MetaphorInstallOwner.Owner',
            [Reflection.TypeAttributes] 'Public, Sealed, Class', [object], [Type[]] @([Windows.Forms.IWin32Window]))
        $field = $type.DefineField('handle', [IntPtr], [Reflection.FieldAttributes]::Private)
        $constructor = $type.DefineConstructor([Reflection.MethodAttributes] 'Public, HideBySig', [Reflection.CallingConventions]::Standard,
            [Type[]] @([IntPtr]))
        $il = $constructor.GetILGenerator()
        $il.Emit([Reflection.Emit.OpCodes]::Ldarg_0)
        $il.Emit([Reflection.Emit.OpCodes]::Call, [object].GetConstructor([Type]::EmptyTypes))
        $il.Emit([Reflection.Emit.OpCodes]::Ldarg_0)
        $il.Emit([Reflection.Emit.OpCodes]::Ldarg_1)
        $il.Emit([Reflection.Emit.OpCodes]::Stfld, $field)
        $il.Emit([Reflection.Emit.OpCodes]::Ret)
        $getter = $type.DefineMethod('get_Handle', [Reflection.MethodAttributes] 'Public, Virtual, Final, NewSlot, HideBySig, SpecialName',
            [IntPtr], [Type]::EmptyTypes)
        $il = $getter.GetILGenerator()
        $il.Emit([Reflection.Emit.OpCodes]::Ldarg_0)
        $il.Emit([Reflection.Emit.OpCodes]::Ldfld, $field)
        $il.Emit([Reflection.Emit.OpCodes]::Ret)
        $type.DefineMethodOverride($getter, [Windows.Forms.IWin32Window].GetMethod('get_Handle'))
        $property = $type.DefineProperty('Handle', [Reflection.PropertyAttributes]::None, [IntPtr], [Type]::EmptyTypes)
        $property.SetGetMethod($getter)
        $script:OwnerType = $type.CreateType()
    }
    $script:OwnerType
}

# An IWin32Window that names HANDLE as a dialog's owner, or $null for no window (0).
function New-DialogOwner([IntPtr] $Handle) {
    if ($Handle -eq [IntPtr]::Zero) { return $null }
    (Get-OwnerType)::new($Handle)
}

# The file chosen in Windows' Open dialog, titled TITLE and showing only files named FILENAME, owned by the console
# window (CONSOLEWINDOW gives its handle) [NEEDS VERIFICATION] by ear (#573); $null when cancelled. SHOWDIALOG shows
# the dialog with its owner, or none, and stands in for it in tests. It throws where the dialog can't open: it needs a
# single-threaded apartment, which Windows PowerShell 5.1's console has.
function Show-OpenFileBox([string] $Title, [string] $FileName,
    [scriptblock] $ConsoleWindow = { (Get-DialogNative)::GetConsoleWindow() },
    [scriptblock] $ShowDialog = {
        param($Dialog, $Owner)
        if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { throw 'the Open dialog needs an STA thread' }
        if ($Owner) { $Dialog.ShowDialog($Owner) } else { $Dialog.ShowDialog() }
    }) {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object Windows.Forms.OpenFileDialog
    try {
        $dialog.Title = $Title
        $dialog.Filter = "$FileName|$FileName"
        $dialog.CheckFileExists = $true
        $owner = New-DialogOwner (& $ConsoleWindow)
        if ((& $ShowDialog $dialog $owner) -eq [Windows.Forms.DialogResult]::OK) { return $dialog.FileName }
        return $null
    }
    finally { $dialog.Dispose() }
}

# How install.ps1 asks its questions: in windows (DIALOGS), else typed in the console, or from ANSWERS in order with
# SCRIPTED. VOICE (Start-Voice's, or $null) speaks each typed question; a question in a window is spoken only when
# SCREENREADER is off, since a running reader reads the window itself. MESSAGEBOX and PICKFILE stand in for
# Show-MessageBox and Show-OpenFileBox in tests.
function New-InstallAsker([string[]] $Answers, [switch] $Scripted, $Voice, [switch] $ScreenReader, [switch] $Dialogs,
    [scriptblock] $MessageBox = { param($Text, $Caption, $Flags) Show-MessageBox $Text $Caption $Flags },
    [scriptblock] $PickFile = { param($Title, $FileName) Show-OpenFileBox $Title $FileName }) {
    $queue = $null
    if ($Scripted) {
        $queue = [Collections.Generic.Queue[string]]::new()
        foreach ($answer in $Answers) { $queue.Enqueue($answer) }
    }
    [pscustomobject]@{ Answers = $queue; Voice = $Voice; ScreenReader = $ScreenReader.IsPresent
        Dialogs = ($Dialogs.IsPresent -and -not $Scripted); MessageBox = $MessageBox; PickFile = $PickFile }
}

# Speaks QUESTION, then returns the typed answer: from ASKER's answers, or else the console (Read-Answer). '' for Enter,
# $null for none, which counts as no.
function Read-AskerAnswer($Asker, [string] $Question) {
    if ($Asker.Voice) { $Asker.Voice.Speak(($Question -replace '\s*\[[^\]]*\]$', '')) }
    if ($null -eq $Asker.Answers) { return Read-Answer $Question }
    Write-Host "$Question " -NoNewline
    if ($Asker.Answers.Count -eq 0) { Write-Host '(no answer given, so no)'; return $null }
    $answer = $Asker.Answers.Dequeue()
    Write-Host $answer
    $answer
}

# Speaks a question in a window when no screen reader will read it.
function Write-WindowQuestion($Asker, [string] $Question) {
    if ($Asker.Voice -and -not $Asker.ScreenReader) { $Asker.Voice.Speak($Question) }
}

# True for yes to QUESTION, asked through ASKER: Yes or No in a message box, the default button Yes with
# ENTERMEANSYES; else typed (Format-YesNoQuestion), where Enter alone means no, or yes with ENTERMEANSYES. The window's
# question is printed with its answer once it closes, so the reader isn't sent a new line while the box is open.
function Confirm-Question($Asker, [string] $Question, [switch] $EnterMeansYes,
    [ValidateSet('stop', 'skip it')] [string] $Ending = 'stop') {
    if ($Asker.Dialogs) {
        Write-WindowQuestion $Asker $Question
        $flags = $MessageBoxFlag.YesNo -bor $MessageBoxFlag.Question -bor $MessageBoxFlag.SetForeground
        if (-not $EnterMeansYes) { $flags = $flags -bor $MessageBoxFlag.SecondDefault }
        try { $button = & $Asker.MessageBox $Question $QuestionCaption ([UInt32] $flags) }
        catch { Write-Verbose "the question window did not open: $_"; $button = 0 }
        if ($button -eq $MessageBoxResult.Yes) { Write-Host "$Question Yes"; return $true }
        if ($button -eq $MessageBoxResult.No) { Write-Host "$Question No"; return $false }
    }
    $answer = Read-AskerAnswer $Asker (Format-YesNoQuestion $Question -EnterMeansYes:$EnterMeansYes -Ending $Ending)
    if ($EnterMeansYes) { Test-YesOrEnter $answer } else { Test-Yes $answer }
}

# The file named FILENAME chosen through ASKER: in the Open dialog titled TITLE, or else typed in answer to TYPED. '' when
# none was chosen. The caller checks what comes back.
function Select-AskerFile($Asker, [string] $Title, [string] $FileName, [string] $Typed) {
    if ($Asker.Dialogs) {
        Write-WindowQuestion $Asker $Title
        try {
            $chosen = & $Asker.PickFile $Title $FileName
            Write-Host "$Title $(if ($chosen) { $chosen } else { '(none chosen)' })"
            return [string] $chosen
        }
        catch { Write-Verbose "the Open dialog did not open: $_" }
    }
    $answer = Read-AskerAnswer $Asker $Typed
    if ($answer) { $answer.Trim().Trim('"') } else { '' }
}

# setup-wsl.ps1's exit code when this machine can't run WSL 2 yet; onboard.ps1 then gives the advice to install WSL
# (#360). Every other failure exits 1.
$WslUnavailableExitCode = 2

# --- Finding the game and Reloaded-II --------------------------------------------------------------------------------
# Finds the game and Reloaded-II on this machine (#267), for onboard.ps1 and the players' installer (install.ps1, #303).
# It reads only. The players' install check (#283) doesn't use it, so it ignores the developer variables.
#
# The game: METAPHOR_GAME_EXE, then the game registered in Reloaded-II (the precedence scripts/dev/session.ps1
# uses, through GameSession.psm1), then METAPHOR.exe in Steam's libraries. How Steam lists its
# libraries: docs/raw-sources/steam-library-folders-2026-11-18.md.
# Reloaded-II: METAPHOR_RELOADED_EXE, then LauncherPath in ReloadedII.json, which every launch rewrites
# (docs/raw-sources/reloaded-ii-config-location-2026-11-13.md). There's no install folder to probe: Reloaded-II is a
# Release.zip extracted wherever the user chooses (docs/raw-sources/reloaded-ii-log-on-normal-run-2026-11-14.md, step
# 2), so one that has never been launched isn't found.

$GameExeName = 'METAPHOR.exe'

# Steam's folder from the registry, or $null when Steam isn't installed.
function Get-SteamRoot {
    $key = Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue
    if ($key -and $key.PSObject.Properties['SteamPath']) { return [IO.Path]::GetFullPath($key.SteamPath) }
    return $null
}

# Each Steam library folder that exists: Steam's own, then those in libraryfolders.vdf, once each.
function Get-SteamLibrary([string] $SteamRoot) {
    if (-not $SteamRoot -or -not (Test-Path -LiteralPath $SteamRoot -PathType Container)) { return }
    $folders = [Collections.Generic.List[string]]::new()
    $folders.Add($SteamRoot)
    $vdf = Join-Path $SteamRoot 'steamapps/libraryfolders.vdf'
    if (Test-Path -LiteralPath $vdf) {
        # UTF-8 without a byte order mark, as Read-Json's files are.
        foreach ($line in Get-Content -Encoding UTF8 -LiteralPath $vdf) {
            if ($line -match '^\s*"path"\s+"(.+)"\s*$') { $folders.Add(($Matches[1] -replace '\\\\', '\')) }
        }
    }
    $seen = @{}
    foreach ($folder in $folders) {
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) { continue }
        $full = [IO.Path]::GetFullPath($folder).TrimEnd('\')
        if (-not $seen.ContainsKey($full)) { $seen[$full] = $true; $full }
    }
}

# Each game exe in a Steam library's steamapps\common\<folder>, whatever the folder is called.
function Find-SteamGame([string] $SteamRoot) {
    foreach ($library in Get-SteamLibrary $SteamRoot) {
        $common = Join-Path $library 'steamapps\common'
        if (-not (Test-Path -LiteralPath $common)) { continue }
        foreach ($folder in Get-ChildItem -LiteralPath $common -Directory) {
            $exe = Join-Path $folder.FullName $GameExeName
            if (Test-Path -LiteralPath $exe -PathType Leaf) { $exe }
        }
    }
}

# The game and launcher Reloaded-II records (Resolve-LoaderPaths), each $null when it isn't recorded. The environment
# variables are left to the callers, which ask about them differently.
function Get-LoaderRecord([string] $ConfigPath) {
    $none = [pscustomobject]@{ GameExe = $null; LauncherExe = $null }
    if (-not $ConfigPath -or -not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { return $none }
    try { return Resolve-LoaderPaths (Read-Json $ConfigPath) $null $null } catch { return $none }
}

function Test-File([string] $Path) { [bool]($Path -and (Test-Path -LiteralPath $Path -PathType Leaf)) }

# Reloaded-II's launcher: METAPHOR_RELOADED_EXE, then the config's LauncherPath; $null when neither is a file named
# Reloaded-II.exe, so a record pointing at another program is never started or linked to. IGNOREENVIRONMENT skips
# METAPHOR_RELOADED_EXE, a developer's override the players' installer never follows (#408).
function Find-ReloadedLauncher([string] $ConfigPath = (Get-ReloadedConfigPath), [switch] $IgnoreEnvironment) {
    $override = if ($IgnoreEnvironment) { $null } else { $env:METAPHOR_RELOADED_EXE }
    foreach ($launcher in $override, (Get-LoaderRecord $ConfigPath).LauncherExe) {
        if ((Test-File $launcher) -and (Split-Path -Leaf $launcher) -eq 'Reloaded-II.exe') { return $launcher }
    }
    return $null
}

# Each game exe that exists, as a full path, once: the one Reloaded-II has registered, then those in Steam's libraries.
# The environment variables are left to the callers.
function Find-MetaphorGame([string] $SteamRoot = (Get-SteamRoot), [string] $ConfigPath = (Get-ReloadedConfigPath)) {
    $found = [Collections.Generic.List[string]]::new()
    foreach ($exe in @((Get-LoaderRecord $ConfigPath).GameExe) + @(Find-SteamGame $SteamRoot)) {
        if (-not (Test-File $exe)) { continue }
        $full = [IO.Path]::GetFullPath($exe)
        if (-not ($found | Where-Object { $_ -eq $full })) { $found.Add($full) }
    }
    $found
}

# The game exe to use, as { Path; Saved }: Saved is true when it came from METAPHOR_GAME_EXE. $null when there is none.
# Otherwise it offers what Reloaded-II and Steam know of: one is shown and confirmed (Enter means yes), several are listed to pick
# from, none asks for the path. ASK takes a question and returns the answer, '' for Enter, or $null for no answer
# (no console), which counts as no.
function Select-MetaphorGame([scriptblock] $Ask, [string] $SteamRoot = (Get-SteamRoot),
    [string] $ConfigPath = (Get-ReloadedConfigPath)) {
    if (Test-File $env:METAPHOR_GAME_EXE) {
        Write-Host "METAPHOR_GAME_EXE already points at the game: $env:METAPHOR_GAME_EXE"
        return [pscustomobject]@{ Path = $env:METAPHOR_GAME_EXE; Saved = $true }
    }
    if ($env:METAPHOR_GAME_EXE) { Write-Host 'METAPHOR_GAME_EXE points at a file that is not there; looking again.' }

    $found = @(Find-MetaphorGame -SteamRoot $SteamRoot -ConfigPath $ConfigPath)
    $path = $null
    if ($found.Count -eq 1) {
        Write-Host "Found the game: $($found[0])"
        $question = 'Use it, saving its path in your Windows user''s METAPHOR_GAME_EXE environment variable?'
        if (Test-YesOrEnter (& $Ask (Format-YesNoQuestion $question -EnterMeansYes -Ending 'skip it'))) { $path = $found[0] }
    }
    elseif ($found.Count -gt 1) {
        Write-Host 'Found the game in more than one place:'
        for ($i = 0; $i -lt $found.Count; $i++) { Write-Host "  $($i + 1). $($found[$i])" }
        $answer = & $Ask "Which one? [1-$($found.Count), Enter to skip]"
        if ($answer -match '^\s*\d+\s*$' -and [int]$answer -ge 1 -and [int]$answer -le $found.Count) {
            $path = $found[[int]$answer - 1]
        }
    }
    else {
        Write-Host "Did not find $GameExeName in Reloaded-II or a Steam library. You do not need the game to contribute."
        $answer = & $Ask "Full path to $GameExeName (Enter to skip):"
        $typed = if ($answer) { $answer.Trim().Trim('"') } else { '' }
        if ($typed -and (Split-Path -Leaf $typed) -eq $GameExeName -and (Test-File $typed)) { $path = $typed }
        elseif ($typed) { Write-Host "That is not a $GameExeName that exists; skipping the game." }
    }
    if ($path) { return [pscustomobject]@{ Path = $path; Saved = $false } }
    return $null
}

# --- The install check -----------------------------------------------------------------------------------------------
# The players' install check (#283): says why the mod didn't load, for the one case the mod can't report on itself.
# check-install.ps1 prints and speaks what Get-InstallCheck finds. It reads files and lists processes and windows; it
# changes no file or setting. It reads nothing over the network: a Reloaded-II path on a network share or mapped
# network drive is reported, not read.
#
# The idea of a checker players run on their own install comes from BMMValidator
# (https://gitlab.com/BMMProject/Tools/BMMValidator, Apache-2.0, at d69404ee), a declarative install checker for other
# games' accessibility mods. No code is taken (docs/raw-sources/install-check-sources-2026-11-19.md).
#
# What it reads, all from the release folder it runs from (the package's own files, src/Metaphor.Hooks) and from
# Reloaded-II's records:
# - ReloadedII.json at its fixed AppData path, and the launcher its LauncherPath names;
# - the mods folder it names (ModConfigDirectory), for a ModConfig.json with this release's ModId, and the game's
#   AppConfig.json in the applications folder, for AppLocation and EnabledMods
#   (docs/raw-sources/reloaded-ii-config-location-2026-11-13.md, reloaded-ii-app-and-mod-config-2026-11-15.md);
# - the game exe's SHA-256 against known-game-builds.json, the same list the mod embeds (src/Metaphor.Core);
# - prism.dll's SHA-256 against prism.json, the pin the mod checks it by (ADR 0005);
# - mod-status.json, which the mod writes into its own folder at each start (Metaphor.Core.ModStatusFile);
# - running screen readers, as Prism finds them (Find-ScreenReader).
#
# Each check has a Line, spoken and printed, in plain words with no path or hash, and a Detail, printed only, with
# the path or hash a helper may ask for.

# The readers the mod speaks through before Windows' own voices (ADR 0005's fixed list).
$SupportedReaders = 'NVDA, JAWS or ZoomText'
$CopyAgain = 'Copy the mod folder from the release zip into Reloaded-II''s mods folder again, with every file in it.'
$RunFromRelease = 'Run check-install.ps1 from the mod folder in the release zip.'
$KeepLocal = 'Keep Reloaded-II and the game on this computer''s own drives. This check does not read over the network.'

# Each running screen reader the mod supports, by name. JAWS and ZoomText by the windows Prism v0.18.3 looks for
# before it uses them; NVDA by its program, nvda.exe (docs/raw-sources/install-check-sources-2026-11-19.md). Prism
# asks NVDA itself over RPC, which a script can't do simply.
function Find-ScreenReader {
    if (@(Get-Process -Name nvda -ErrorAction SilentlyContinue).Count -gt 0) { 'NVDA' }
    try {
        $window = Get-FindWindow
        if ($window::FindWindow('JFWUI2', $null) -ne [IntPtr]::Zero) { 'JAWS' }
        if ($window::FindWindow('ZXSPEECHWNDCLASS', 'ZoomText Speech Processor') -ne [IntPtr]::Zero) { 'ZoomText' }
    }
    catch { Write-Verbose "could not look for JAWS and ZoomText windows: $_" }
}

# A type with user32's FindWindowW, built in memory (#459). Add-Type would compile it, and in Windows PowerShell 5.1
# the compiler writes to the temp folder and fails when that folder's path has a character outside the system's ANSI
# code page (a user name in another script), printing an error to the player.
$FindWindowType = $null
function Get-FindWindow {
    if (-not $script:FindWindowType) {
        $access = [Reflection.Emit.AssemblyBuilderAccess]::Run
        $assembly = [Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly((New-Object Reflection.AssemblyName 'MetaphorInstallCheck'), $access)
        $type = $assembly.DefineDynamicModule('MetaphorInstallCheck').DefineType('MetaphorInstallCheck.Window',
            [Reflection.TypeAttributes] 'Public, Abstract, Sealed')
        $method = $type.DefinePInvokeMethod('FindWindow', 'user32.dll', 'FindWindowW',
            [Reflection.MethodAttributes] 'Public, Static, PinvokeImpl', [Reflection.CallingConventions]::Standard, [IntPtr],
            [Type[]] @([string], [string]), [Runtime.InteropServices.CallingConvention]::Winapi,
            [Runtime.InteropServices.CharSet]::Unicode)
        $method.SetImplementationFlags([Reflection.MethodImplAttributes]::PreserveSig)
        $script:FindWindowType = $type.CreateType()
    }
    $script:FindWindowType
}

# How to speak: with Windows' voice (Speak), through the running screen reader (Reader, its name), or neither, and the
# line saying why not. -NoSpeech wins, then -Speak. Without a console it prints only. With a screen reader running the
# lines go through the reader itself (#601): it doesn't reliably read new lines in the window, and two voices at once
# can't be followed.
function Select-Speech([bool] $Speak, [bool] $NoSpeech, [bool] $Interactive, [string[]] $Readers) {
    $result = { param($On, $Why, $Reader) [pscustomobject]@{ Speak = $On; Message = $Why; Reader = $Reader } }
    if ($NoSpeech) { return & $result $false $null $null }
    if ($Speak) { return & $result $true $null $null }
    if (-not $Interactive) { return & $result $false 'No console, so printing only.' $null }
    if (@($Readers).Count -gt 0) { return & $result $false $null @($Readers)[0] }
    & $result $true $null $null
}

# The screen readers the installer speaks through, in the order it tries them: the first three of the mod's fixed list
# (ADR 0005; src/Metaphor.Hooks/PrismSpeech.cs), with Prism v0.18.3's IDs
# (docs/raw-sources/installer-prism-speech-2026-11-20.md). Never a backend that loads a DLL by bare name.
$PrismReaders = @(
    @{ Name = 'NVDA'; Id = [UInt64]::Parse('89CC19C5C4AC1A56', 'AllowHexSpecifier') },
    @{ Name = 'JAWS'; Id = [UInt64]::Parse('AC3D60E9BD84B53E', 'AllowHexSpecifier') },
    @{ Name = 'ZoomText'; Id = [UInt64]::Parse('AE439D62DC7B1479', 'AllowHexSpecifier') }
)

# A type with prism.dll's calls, bound to the file at PATH by its full path, and kernel32's LoadLibraryExW, built in
# memory as Get-FindWindow is (#459). One per path.
$PrismTypes = @{}
function Get-PrismNative([string] $Path) {
    if (-not $script:PrismTypes.ContainsKey($Path)) {
        $name = "MetaphorInstallPrism$($script:PrismTypes.Count)"
        $access = [Reflection.Emit.AssemblyBuilderAccess]::Run
        $assembly = [Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly((New-Object Reflection.AssemblyName $name), $access)
        $type = $assembly.DefineDynamicModule($name).DefineType("$name.Native", [Reflection.TypeAttributes] 'Public, Abstract, Sealed')
        $calls = @(
            @('kernel32.dll', 'LoadLibraryExW', [IntPtr], @([string], [IntPtr], [UInt32]), 'Winapi'),
            @($Path, 'prism_init', [IntPtr], @([IntPtr]), 'Cdecl'),
            @($Path, 'prism_shutdown', [void], @([IntPtr]), 'Cdecl'),
            @($Path, 'prism_registry_create', [IntPtr], @([IntPtr], [UInt64]), 'Cdecl'),
            @($Path, 'prism_backend_initialize', [Int32], @([IntPtr]), 'Cdecl'),
            @($Path, 'prism_backend_free', [void], @([IntPtr]), 'Cdecl'),
            # Text is NUL-terminated UTF-8; C's bool is one byte.
            @($Path, 'prism_backend_speak', [Int32], @([IntPtr], [byte[]], [byte]), 'Cdecl'))
        foreach ($call in $calls) {
            $method = $type.DefinePInvokeMethod($call[1], $call[0], $call[1], [Reflection.MethodAttributes] 'Public, Static, PinvokeImpl',
                [Reflection.CallingConventions]::Standard, $call[2], [Type[]] $call[3],
                [Runtime.InteropServices.CallingConvention] $call[4], [Runtime.InteropServices.CharSet]::Unicode)
            $method.SetImplementationFlags([Reflection.MethodImplAttributes]::PreserveSig)
        }
        $script:PrismTypes[$Path] = $type.CreateType()
    }
    $script:PrismTypes[$Path]
}

# Loads prism.dll from its full PATH, searching only its own folder and System32 for what it needs
# (LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32), never PATH or the working folder, and returns its
# calls: Init, Shutdown, Create, Initialize, Free and Speak. Throws when it doesn't load.
function Open-Prism([string] $Path) {
    $native = Get-PrismNative $Path
    if ($native::LoadLibraryExW($Path, [IntPtr]::Zero, 0x900) -eq [IntPtr]::Zero) { throw 'prism.dll did not load' }
    # Binds each call now, to the library just loaded by the same full path.
    foreach ($method in $native.GetMethods()) { if ($method.Name -like 'prism_*') { [Runtime.InteropServices.Marshal]::Prelink($method) } }
    $prism = [pscustomobject]@{ Native = $native }
    $prism | Add-Member ScriptMethod Init { $n = $this.Native; $n::prism_init([IntPtr]::Zero) }
    $prism | Add-Member ScriptMethod Shutdown { param($Context) $n = $this.Native; $n::prism_shutdown($Context) }
    $prism | Add-Member ScriptMethod Create { param($Context, $Id) $n = $this.Native; $n::prism_registry_create($Context, [UInt64] $Id) }
    $prism | Add-Member ScriptMethod Initialize { param($Backend) $n = $this.Native; $n::prism_backend_initialize($Backend) }
    $prism | Add-Member ScriptMethod Free { param($Backend) $n = $this.Native; $n::prism_backend_free($Backend) }
    $prism | Add-Member ScriptMethod Speak {
        param($Backend, [string] $Text, [bool] $Interrupt)
        $n = $this.Native
        $n::prism_backend_speak($Backend, [Text.Encoding]::UTF8.GetBytes($Text + [char] 0), [byte] $Interrupt)
    }
    $prism
}

# Speech through the running screen reader, with the prism.dll the release ships in FOLDER (#601), or $null when it
# can't start: no prism.dll or prism.json, a prism.dll other than the pinned one, or no screen reader that starts.
# Prism is loaded by full path, held open from the hash check until it has loaded, as the mod loads it (ADR 0005).
# The speaker has Speak, which queues the line after what the reader is saying, Dispose, and Voice.Name, the reader's.
# OPENPRISM stands in for Open-Prism in tests.
function New-ReaderSpeech([string] $Folder, [scriptblock] $OpenPrism = { param($Path) Open-Prism $Path }) {
    $path = [IO.Path]::GetFullPath((Join-Path $Folder 'prism.dll'))
    $pin = [string] (Get-Property (Read-JsonOrNull (Join-Path $Folder 'prism.json')) 'dllSha256')
    if (-not $pin -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { Write-Verbose 'no prism.dll or prism.json'; return $null }
    try {
        $held = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try {
            if ((Get-StreamSha256 $held) -ne $pin) { Write-Verbose 'prism.dll is not the pinned one'; return $null }
            $prism = & $OpenPrism $path
        }
        finally { $held.Dispose() }
        $context = $prism.Init()
        if ($context -eq [IntPtr]::Zero) { Write-Verbose 'prism_init failed'; return $null }
        foreach ($reader in $script:PrismReaders) {
            $backend = $prism.Create($context, $reader.Id)
            if ($backend -eq [IntPtr]::Zero) { continue }
            if ($prism.Initialize($backend) -eq 0) { return New-ReaderSpeaker $prism $context $backend $reader.Name }
            $prism.Free($backend)
        }
        $prism.Shutdown($context)
    }
    catch { Write-Verbose "Prism did not start: $_" }
    $null
}

# The speaker New-ReaderSpeech returns, over PRISM's started BACKEND for the reader NAME.
function New-ReaderSpeaker($Prism, $Context, $Backend, [string] $Name) {
    $speaker = [pscustomobject]@{ Prism = $Prism; Context = $Context; Backend = $Backend
        Voice = [pscustomobject]@{ Name = $Name; Culture = $null } }
    $speaker | Add-Member ScriptMethod Speak {
        param([string] $Text)
        if ($this.Backend -eq [IntPtr]::Zero) { return }
        # Not interrupting, so a line never cuts off the one before it.
        $status = $this.Prism.Speak($this.Backend, $Text, $false)
        if ($status -ne 0) { Write-Verbose "the screen reader did not take the line (Prism error $status)" }
    }
    $speaker | Add-Member ScriptMethod Dispose {
        if ($this.Backend -eq [IntPtr]::Zero) { return }
        $this.Prism.Free($this.Backend)
        $this.Prism.Shutdown($this.Context)
        $this.Backend = [IntPtr]::Zero
    }
    $speaker
}

# Windows' own voice through System.Speech (SAPI), or $null when it can't start. NEWSYNTHESIZER stands in for the
# SpeechSynthesizer in tests. The lines are English, so it uses an English voice (Select-EnglishVoice).
function New-Voice([scriptblock] $NewSynthesizer) {
    try {
        if (-not $NewSynthesizer) {
            Add-Type -AssemblyName System.Speech
            $NewSynthesizer = { New-Object System.Speech.Synthesis.SpeechSynthesizer }
        }
        $voice = & $NewSynthesizer
        $voice.SetOutputToDefaultAudioDevice()
    }
    catch { return $null }
    Select-EnglishVoice $voice
    $voice
}

# True for an English language tag: en, en-US, en-GB and the rest.
function Test-EnglishTag([string] $Tag) { [bool]($Tag -match '^en(-|$)') }

# True for an English culture: one whose name is an English tag.
function Test-EnglishCulture($Culture) { [bool]($Culture -and (Test-EnglishTag ([string] $Culture.Name))) }

# The order English voices are chosen in: en-US, then en-GB, then any other.
function Get-EnglishRank([string] $Tag) { switch ($Tag) { 'en-US' { 0 } 'en-GB' { 1 } default { 2 } } }

# Keeps VOICE's default voice when it is English; otherwise selects an installed, enabled English one, en-US first,
# then en-GB, then any other. On a French, German or Japanese Windows the default voice would read the English lines
# with its own language's pronunciation (#457). With no English voice, or one that won't select, the default stays.
function Select-EnglishVoice($Voice) {
    try {
        if (Test-EnglishCulture $Voice.Voice.Culture) { return }
        $english = @($Voice.GetInstalledVoices() | Where-Object { $_.Enabled -and (Test-EnglishCulture $_.VoiceInfo.Culture) } |
                Sort-Object { Get-EnglishRank ([string] $_.VoiceInfo.Culture.Name) })
        if ($english.Count -gt 0) { $Voice.SelectVoice($english[0].VoiceInfo.Name) }
    }
    catch { Write-Verbose "kept the default voice: $_" }
}

# How long a OneCore voice may take to turn one line into sound before the installer gives up on it.
$script:OneCoreSynthesisTimeoutMs = 10000

# SYNTH's sound for TEXT, as a WAV file in a MemoryStream; it throws when the voice fails or takes longer than TIMEOUTMS.
# SYNTH is a Windows.Media.SpeechSynthesis.SpeechSynthesizer; its stream is "audio/wav" and the Windows Runtime's
# asynchronous call is waited on through .NET's AsTask (docs/raw-sources/onecore-speech-sources-2026-11-19.md).
function Get-OneCoreWave($Synth, [string] $Text, [int] $TimeoutMs = $script:OneCoreSynthesisTimeoutMs) {
    $asTask = @([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
            $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetGenericArguments().Count -eq 1 -and
            @($_.GetParameters()).Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
    $streamType = [Windows.Media.SpeechSynthesis.SpeechSynthesisStream, Windows.Media.SpeechSynthesis, ContentType = WindowsRuntime]
    $operation = $Synth.SynthesizeTextToStreamAsync($Text)
    $task = $asTask.MakeGenericMethod($streamType).Invoke($null, @($operation))
    if (-not $task.Wait($TimeoutMs)) {
        try { $operation.Cancel() } catch { Write-Verbose "could not cancel the voice: $_" }
        throw "the voice took longer than $TimeoutMs ms"
    }
    $sound = $task.Result
    try {
        $wave = New-Object IO.MemoryStream
        $read = [IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($sound)
        try { $read.CopyTo($wave) } finally { $read.Dispose() }
        $wave.Position = 0
        $wave
    }
    finally { $sound.Dispose() }
}

# Windows' OneCore voices through the Windows Runtime's SpeechSynthesizer, which Windows PowerShell 5.1 reaches and
# System.Speech does not: System.Speech sees only the older "Desktop" voices, not the ones Windows Settings adds (#509).
# Throws when the Windows Runtime or its voices aren't there, as in PowerShell 7. The engine has the installed voices
# (AllVoices: DisplayName and a Language tag), the one in use (Voice), SpeakText, which returns once the line is said,
# and Dispose; tests stand in for it with the same members.
function New-OneCoreEngine {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $type = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType = WindowsRuntime]
    $voices = @($type::AllVoices)
    if ($voices.Count -eq 0) { throw 'Windows has no OneCore voice' }
    $engine = [pscustomobject]@{ Synth = (New-Object $type); AllVoices = $voices }
    $engine | Add-Member ScriptProperty Voice { $this.Synth.Voice } { param($Value) $this.Synth.Voice = $Value }
    $engine | Add-Member ScriptMethod SpeakText {
        param($Text)
        $wave = Get-OneCoreWave $this.Synth $Text
        # SoundPlayer plays a WAV file from a stream; PlaySync returns when it has finished, as System.Speech's Speak does.
        $player = New-Object System.Media.SoundPlayer $wave
        try { $player.PlaySync() } finally { $player.Dispose(); $wave.Dispose() }
    }
    $engine | Add-Member ScriptMethod Dispose { $this.Synth.Dispose() }
    $engine
}

# A OneCore engine (New-OneCoreEngine, or NEWONECORE in tests) with an English voice when it has one, picked as
# Select-EnglishVoice picks, or $null when it can't start.
function New-OneCoreVoice([scriptblock] $NewOneCore) {
    try { $engine = if ($NewOneCore) { & $NewOneCore } else { New-OneCoreEngine } }
    catch { Write-Verbose "no OneCore voice: $_"; return $null }
    if (-not $engine) { return $null }
    try {
        if (-not (Test-EnglishTag ([string] $engine.Voice.Language))) {
            $english = @($engine.AllVoices | Where-Object { Test-EnglishTag ([string] $_.Language) } |
                    Sort-Object { Get-EnglishRank ([string] $_.Language) })
            if ($english.Count -gt 0) { $engine.Voice = $english[0] }
        }
    }
    catch { Write-Verbose "kept the default OneCore voice: $_" }
    $engine
}

# The installer's voice over a OneCore ENGINE: Speak and Dispose as System.Speech's synthesizer has, and Voice's Name
# and Culture. When the engine fails to say a line, the voice moves to System.Speech (New-Voice, with NEWSYNTHESIZER)
# and says the line there; when that won't start either, it prints that, once, and says nothing from then on.
function New-OneCoreSpeaker($Engine, [scriptblock] $NewSynthesizer) {
    $speaker = [pscustomobject]@{
        Engine = $Engine; Fallback = $null; Silent = $false; NewSynthesizer = $NewSynthesizer
        Voice = [pscustomobject]@{ Name = [string] $Engine.Voice.DisplayName
            Culture = [pscustomobject]@{ Name = [string] $Engine.Voice.Language } }
    }
    $speaker | Add-Member ScriptMethod Speak {
        param($Text)
        if ($this.Silent) { return }
        if (-not $this.Fallback) {
            try { $this.Engine.SpeakText($Text); return }
            catch {
                Write-Verbose "the OneCore voice failed to speak: $_"
                try { $this.Engine.Dispose() } catch { Write-Verbose "could not close the OneCore voice: $_" }
                $this.Fallback = New-Voice $this.NewSynthesizer
            }
        }
        if ($this.Fallback) {
            try { $this.Fallback.Speak($Text); return }
            catch {
                Write-Verbose "the voice failed to speak: $_"
                try { $this.Fallback.Dispose() } catch { Write-Verbose "could not close the voice: $_" }
            }
        }
        $this.Silent = $true
        Write-Host 'Windows'' voice did not start, so the installer will only print from here on.'
    }
    $speaker | Add-Member ScriptMethod Dispose {
        try { if ($this.Fallback) { $this.Fallback.Dispose() } else { $this.Engine.Dispose() } }
        catch { Write-Verbose "could not close the voice: $_" }
    }
    $speaker
}

# The installer's voice: an English OneCore voice first (New-OneCoreVoice), then an English System.Speech one
# (New-Voice), then whichever of the two started, OneCore first; $null when neither did.
function Select-InstallerVoice([scriptblock] $NewSynthesizer, [scriptblock] $NewOneCore) {
    $engine = New-OneCoreVoice $NewOneCore
    if ($engine -and (Test-EnglishTag ([string] $engine.Voice.Language))) { return New-OneCoreSpeaker $engine $NewSynthesizer }
    $sapi = New-Voice $NewSynthesizer
    $sapiEnglish = $false
    if ($sapi) { try { $sapiEnglish = Test-EnglishCulture $sapi.Voice.Culture } catch { Write-Verbose "could not read the voice's language: $_" } }
    if ($sapi -and ($sapiEnglish -or -not $engine)) {
        if ($engine) { try { $engine.Dispose() } catch { Write-Verbose "could not close the OneCore voice: $_" } }
        return $sapi
    }
    if ($sapi) { try { $sapi.Dispose() } catch { Write-Verbose "could not close the voice: $_" } }
    if ($engine) { return New-OneCoreSpeaker $engine $NewSynthesizer }
    $null
}

# The voice for SPEECH (Select-Speech): the screen reader's, Windows', or $null for printing only. Prints SPEECH's
# message, and says once when Windows' voice is not English. NEWSYNTHESIZER as for New-Voice, NEWONECORE as for
# New-OneCoreVoice, NEWREADER (the speaker, or $null) for New-ReaderSpeech over this module's folder.
function Start-Voice($Speech, [scriptblock] $NewSynthesizer, [scriptblock] $NewOneCore,
    [scriptblock] $NewReader = { New-ReaderSpeech $PSScriptRoot }) {
    if ($Speech.Message) { Write-Host $Speech.Message }
    if ($Speech.PSObject.Properties['Reader'] -and $Speech.Reader) {
        $reader = & $NewReader
        if ($reader) { return $reader }
        Write-Host "The installer could not speak through $($Speech.Reader), so it will only print from here on."
        return $null
    }
    if (-not $Speech.Speak) { return $null }
    $voice = Select-InstallerVoice $NewSynthesizer $NewOneCore
    if (-not $voice) {
        Write-Host 'Windows'' voice did not start, so the installer will only print from here on.'
        return $null
    }
    $culture = $null
    try { $culture = $voice.Voice.Culture } catch { Write-Verbose "could not read the voice's language: $_" }
    if (-not (Test-EnglishCulture $culture)) {
        $line = 'Windows has no English voice to read these lines, so another language''s voice reads them, which may be hard to follow.'
        # A voice added in Settings is a OneCore one, so the installer can use it only when OneCore voices work here.
        if ($voice.PSObject.Properties['Engine']) {
            $line += ' You can add an English voice in Windows Settings, under Time and language, then Speech.'
        }
        Write-Host $line
        # A voice that fails to speak leaves the installer printing only, as one that fails to start does.
        try { $voice.Speak($line) }
        catch {
            Write-Verbose "the voice failed to speak: $_"
            try { $voice.Dispose() } catch { Write-Verbose "could not close the voice: $_" }
            Write-Host 'Windows'' voice did not start, so the installer will only print from here on.'
            return $null
        }
    }
    $voice
}

function New-Check([string] $Name, [string] $State, [string] $Line, [string] $Detail, [string] $Fix) {
    [pscustomobject]@{ Name = $Name; State = $State; Line = $Line; Detail = $Detail; Fix = $Fix }
}
function Pass([string] $Name, [string] $Line, [string] $Detail) { New-Check $Name 'pass' $Line $Detail $null }
function Problem([string] $Name, [string] $Line, [string] $Fix, [string] $Detail) { New-Check $Name 'problem' $Line $Detail $Fix }
function Skip([string] $Name, [string] $Line) { New-Check $Name 'not checked' $Line $null $null }

function Read-JsonOrNull([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    try { return Read-Json $Path } catch { return $null }
}

function Get-Property($Object, [string] $Name) {
    if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) { return $Object.$Name }
    return $null
}

# Every check, in the order a fix is needed: { Name; State (pass, problem, note or not checked); Line; Detail; Fix }. A
# check that needs an earlier one to pass is "not checked". RELEASEFOLDER is the folder the script runs from,
# downloaded or installed; FINDSCREENREADER returns the names of the running readers.
function Get-InstallCheck([string] $ReleaseFolder, [string] $ConfigPath = (Get-ReloadedConfigPath),
    [scriptblock] $FindScreenReader = { Find-ScreenReader }) {
    $modConfig = Read-JsonOrNull (Join-Path $ReleaseFolder 'ModConfig.json')
    $modId = [string] (Get-Property $modConfig 'ModId')
    $modName = [string] (Get-Property $modConfig 'ModName')
    if (-not $modName) { $modName = $modId }

    # Reloaded-II.
    $loader = $null
    $launcher = $null
    if (Test-NetworkPath $ConfigPath) {
        Problem 'Reloaded-II' 'Reloaded-II''s settings file is on a network drive, so this check did not read it.' $KeepLocal "Settings file: $ConfigPath"
    }
    elseif (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
        Problem 'Reloaded-II' 'Reloaded-II is not installed, or has never been started.' 'Install Reloaded-II and start it once.' `
            "Settings file not found: $ConfigPath"
    }
    elseif ($null -eq ($loader = Read-JsonOrNull $ConfigPath)) {
        Problem 'Reloaded-II' 'Reloaded-II''s settings file could not be read.' 'Start Reloaded-II once, so it writes its settings file again.' `
            "Settings file: $ConfigPath"
    }
    elseif (Test-NetworkPath ($launcher = [string] (Get-Property $loader 'LauncherPath'))) {
        Problem 'Reloaded-II' 'Reloaded-II''s program is on a network drive, so this check did not look at it.' $KeepLocal "Program: $launcher"
    }
    elseif (-not $launcher -or -not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
        Problem 'Reloaded-II' 'Reloaded-II''s program is not where Reloaded-II last ran from.' 'Start Reloaded-II once from the folder it is in now.' `
            "Program not found: $launcher"
    }
    else { Pass 'Reloaded-II' 'Reloaded-II is set up.' }

    # The mod in the mods folder.
    $installed = $null
    $mods = [string] (Get-Property $loader 'ModConfigDirectory')
    if (-not $modId) {
        Problem 'Mod installed' 'This script is not in the mod''s folder, so it cannot tell which mod to look for.' $RunFromRelease `
            "No ModConfig.json naming the mod in: $ReleaseFolder"
    }
    elseif ($null -eq $loader) { Skip 'Mod installed' 'Whether the mod is installed, because Reloaded-II is not set up.' }
    elseif (Test-NetworkPath $mods) {
        Problem 'Mod installed' 'Reloaded-II''s mods folder is on a network drive, so this check did not look in it.' $KeepLocal "Mods folder: $mods"
    }
    elseif (-not $mods -or -not (Test-Path -LiteralPath $mods -PathType Container)) {
        Problem 'Mod installed' 'Reloaded-II''s mods folder is missing.' 'Start Reloaded-II once, so it makes its mods folder.' "Mods folder: $mods"
    }
    else {
        $installed = Get-ChildItem -LiteralPath $mods -Directory | Where-Object {
            [string] (Get-Property (Read-JsonOrNull (Join-Path $_.FullName 'ModConfig.json')) 'ModId') -eq $modId
        } | Select-Object -First 1
        if ($installed) { Pass 'Mod installed' 'The mod is in Reloaded-II''s mods folder.' "Folder: $($installed.FullName)" }
        else {
            Problem 'Mod installed' 'The mod is not in Reloaded-II''s mods folder.' `
                "Copy the $modId folder from the release zip into Reloaded-II's mods folder." "Mods folder: $mods"
        }
    }

    # The game in Reloaded-II, and the mod turned on for it.
    $application = $null
    $apps = [string] (Get-Property $loader 'ApplicationConfigDirectory')
    if ($null -eq $loader) { Skip 'Game added' 'Whether the game is in Reloaded-II, because Reloaded-II is not set up.' }
    elseif (Test-NetworkPath $apps) {
        Problem 'Game added' 'Reloaded-II''s list of games is on a network drive, so this check did not read it.' $KeepLocal "Applications folder: $apps"
    }
    else {
        if ($apps -and (Test-Path -LiteralPath $apps -PathType Container)) { $application = Find-MetaphorApplication $loader }
        if ($application) { Pass 'Game added' 'The game is added to Reloaded-II.' }
        else { Problem 'Game added' 'The game is not added to Reloaded-II.' 'In Reloaded-II, add the game''s program, METAPHOR.exe.' }
    }
    $app = if ($application) { Read-JsonOrNull $application.FullName } else { $null }
    if (-not $application) { Skip 'Mod enabled' 'Whether the mod is turned on, because the game is not in Reloaded-II.' }
    elseif (@(Get-Property $app 'EnabledMods') -contains $modId) { Pass 'Mod enabled' 'The mod is turned on for the game.' }
    else {
        Problem 'Mod enabled' 'The mod is not turned on for the game in Reloaded-II.' `
            "Turn the mod on: in Reloaded-II, open the game's page and tick $modName in its list of mods. The install guide has the steps for a screen reader."
    }

    # The game's build.
    $gameBuild = $null
    $location = [string] (Get-Property $app 'AppLocation')
    $gameExe = if ($location) { Resolve-AppLocation $application.FullName $location } else { $null }
    $knownBuilds = Join-Path $ReleaseFolder 'known-game-builds.json'
    if (-not $application) { Skip 'Game version' 'The game''s version, because the game is not in Reloaded-II.' }
    elseif (Test-NetworkPath $gameExe) {
        Problem 'Game version' 'The game is on a network drive, so this check did not read it.' $KeepLocal "Game program: $gameExe"
    }
    elseif (-not $gameExe -or -not (Test-Path -LiteralPath $gameExe -PathType Leaf)) {
        Problem 'Game version' 'The game''s program was not found where Reloaded-II points.' `
            'In Reloaded-II, point the game at METAPHOR.exe in the game''s install folder.' "Reloaded-II points at: $location"
    }
    elseif (-not (Test-Path -LiteralPath $knownBuilds -PathType Leaf)) {
        Problem 'Game version' 'This script''s folder has no list of game versions.' $RunFromRelease "Missing: $knownBuilds"
    }
    else {
        $gameBuild = Get-GameBuild $gameExe $knownBuilds
        if ($gameBuild.Known) {
            Pass 'Game version' "The game's version is one the mod knows, Steam build $(@($gameBuild.Known)[0].steamBuildId)."
        }
        else {
            Problem 'Game version' 'The game is a version this mod does not know yet, so the mod turns itself off.' `
                'Look for a newer release of the mod for this game version.' "Game SHA-256: $($gameBuild.Sha256)"
        }
    }

    # The speech library, in the folder Reloaded-II loads from, or this one while the mod isn't installed.
    $folder = if ($installed) { $installed.FullName } else { $ReleaseFolder }
    $prism = Join-Path $folder 'prism.dll'
    $pin = [string] (Get-Property (Read-JsonOrNull (Join-Path $ReleaseFolder 'prism.json')) 'dllSha256')
    if (-not (Test-Path -LiteralPath $prism -PathType Leaf)) {
        Problem 'Speech library' 'The speech library, prism.dll, is missing from the mod''s folder.' $CopyAgain "Folder: $folder"
    }
    elseif (-not $pin) {
        Problem 'Speech library' 'This script''s folder has nothing to check prism.dll by.' $RunFromRelease "Missing: prism.json in $ReleaseFolder"
    }
    elseif ((Get-Sha256 $prism) -ne $pin) {
        Problem 'Speech library' 'The speech library, prism.dll, is not the one this release ships, so the mod will not use it.' $CopyAgain `
            "File: $prism"
    }
    else { Pass 'Speech library' 'The speech library, prism.dll, is in place.' }

    # The last start.
    $statusFile = if ($installed) { Join-Path $installed.FullName $StatusFileName } else { $null }
    $status = if ($statusFile) { Read-JsonOrNull $statusFile } else { $null }
    $again = 'Start the game through Reloaded-II, then run this check again.'
    if (-not $installed) { Skip 'Last start' 'How the mod last started, because it is not installed.' }
    elseif (-not (Test-Path -LiteralPath $statusFile -PathType Leaf)) {
        Problem 'Last start' 'The mod has not reported a start. Either the game has not been started with the mod since it was installed, or Reloaded-II did not load it.' `
            $again "No $StatusFileName in: $($installed.FullName)"
    }
    elseif ($null -eq $status) { Problem 'Last start' 'The mod''s status file could not be read.' $again "File: $statusFile" }
    elseif ((Get-Property $status 'state') -eq 'ready') {
        $line = "The last time the game started, the mod loaded, with $(Get-Property $status 'hooksLoaded') of $(Get-Property $status 'hooksExpected') hooks."
        if ($gameBuild -and (Get-Property $status 'gameBuildSha256') -ne $gameBuild.Sha256) { $line += ' The game has changed since then.' }
        Pass 'Last start' $line
    }
    else {
        # Only the reason for an unknown build (Metaphor.Core.ModStartup.UnknownGameBuildReason) is spoken: the others
        # can hold an exception's message, with paths in it.
        $reason = [string] (Get-Property $status 'reason')
        $spoken = if ($reason -eq 'unknown game build') { ' because it does not know this game version' } else { '' }
        Problem 'Last start' "The last time the game started, the mod turned itself off$spoken." `
            'Fix the problem the mod gave, then start the game through Reloaded-II again.' "The mod's reason: $reason"
    }

    # A screen reader: a note, not a problem, since the mod falls back to Windows' own voices (ADR 0005).
    $readers = @(& $FindScreenReader)
    if ($readers.Count -gt 0) {
        Pass 'Screen reader' "$($readers -join ' and ') $(if ($readers.Count -gt 1) { 'are' } else { 'is' }) running; the mod speaks through it."
    }
    else {
        New-Check 'Screen reader' 'note' "No screen reader the mod supports is running. In the game the mod speaks through $SupportedReaders; without one it uses Windows' own voice. Start your screen reader before the game to hear the mod through it." $null $null
    }
}

function Get-Label($Check) {
    switch ($Check.State) { 'pass' { 'Pass' } 'problem' { 'Problem' } 'note' { 'Note' } default { 'Not checked' } }
}

# One check as it is printed: the spoken words, then any path or hash.
function Format-Check($Check) {
    $text = "$(Get-Label $Check): $($Check.Line)"
    if ($Check.Detail) { $text += " $($Check.Detail)" }
    $text
}

# One check as it is spoken: words only, no path or hash.
function Format-SpokenCheck($Check) { "$(Get-Label $Check): $($Check.Line)" }

# The last line, printed and spoken: the first problem's fix, or that everything is in order.
function Get-InstallSummary($Checks) {
    $first = @($Checks | Where-Object State -EQ 'problem') | Select-Object -First 1
    if ($first) { return "First thing to fix: $($first.Fix)" }
    'Everything this script checks is in order. If the mod is still silent, report it with the alpha feedback form.'
}

# --- The installer's steps -------------------------------------------------------------------------------------------
# The players' installer's steps (#303); install.ps1 runs them in order, says each one and stops at the first that
# fails. Each step returns { State (done, unchanged or failed); Line, spoken and printed, in plain words; Detail,
# printed only, with a path or code; Fix, what to do when it failed }, and changes nothing that is already right, so a
# second run repairs or updates an install and says what it left alone. It never deletes a file it didn't put there.
#
# Downloads come only from install-pins.json: an https URL (a local file:// one only for the tests, Test-TestSwitch), a
# file name, a SHA-256 and, for the Microsoft installers, the signer; a file that doesn't match is deleted unused. What is pinned and why (Reloaded-II's own Setup.exe
# downloads unpinned files): docs/raw-sources/spoken-installer-sources-2026-11-19.md.
#
# The idea of an installer that also installs Reloaded-II and turns the mod on without Reloaded-II's window comes from
# Persona-4-Golden-Access (https://github.com/AquaRose7/Persona-4-Golden-Access, GPL-3.0, at dcff255), whose installer's
# source isn't published. No code is taken (docs/raw-sources/persona-4-golden-access-2026-11-16.md).

$ShortcutName = 'Metaphor ReFantazio with accessibility.lnk'
$PackageListName = 'package-files.txt'
# Windows Installer's codes for success, success after a restart, and a newer version already installed; and for a
# cancelled install.
$InstallerOk = 0, 3010, 1638
$RestartNeeded = 3010
$Cancelled = 1602

function New-Step([string] $State, [string] $Line, [string] $Detail, [string] $Fix) {
    [pscustomobject]@{ State = $State; Line = $Line; Detail = $Detail; Fix = $Fix }
}
function Done([string] $Line, [string] $Detail) { New-Step 'done' $Line $Detail $null }
function Unchanged([string] $Line, [string] $Detail) { New-Step 'unchanged' $Line $Detail $null }
function Failed([string] $Line, [string] $Fix, [string] $Detail) { New-Step 'failed' $Line $Detail $Fix }

# The step for an error a step stopped with. A write Windows refused (access denied, 0x80070005, which the WScript.Shell
# shortcut save also gives) is most often Windows Security's Controlled folder access, which guards Documents and the
# Desktop, where Reloaded-II and the shortcut may be (#466). How to change its settings is the player's choice, so the
# installer doesn't say.
function Get-ErrorStep($ErrorRecord) {
    $exception = $ErrorRecord.Exception
    while ($exception) {
        if ($exception -is [UnauthorizedAccessException] -or $exception.HResult -eq -2147024891) {
            return Failed 'Windows did not let the installer save this step''s files. Windows Security''s Controlled folder access may have blocked it.' `
                'Windows Security may show a notification about a blocked change; it explains your choices there. Then run the installer again. If it stops here again, report it with the alpha feedback form, with what the installer printed.' "$ErrorRecord"
        }
        $exception = $exception.InnerException
    }
    Failed 'This step stopped with an error.' 'Report it with the alpha feedback form, with what the installer printed.' "$ErrorRecord"
}

# JSON as Reloaded-II writes it: UTF-8 without a byte order mark, which Windows PowerShell 5.1's Set-Content can't do.
# Written beside the file first and then swapped in, so a failed write never leaves half a file; a file replaced is
# kept as PATH.bak. A PATH.bak already there is kept: it is the file from before the installer first changed it (#408).
function Write-JsonFile([string] $Path, $Value) {
    New-Item -ItemType Directory -Force (Split-Path $Path) -ErrorAction Stop | Out-Null
    $temporary = "$Path.tmp"
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $backup = if (Test-Path -LiteralPath "$Path.bak") { [NullString]::Value } else { "$Path.bak" }
        [IO.File]::Replace($temporary, $Path, $backup)
    }
    else { [IO.File]::Move($temporary, $Path) }
}

# True when PATH names Reloaded-II's launcher by its file name; another program is never started or linked to.
function Test-LauncherName([string] $Path) { [bool]($Path -and (Split-Path -Leaf $Path) -eq 'Reloaded-II.exe') }

# The tests' switches, read from the environment (tests/pester/install.Tests.ps1). METAPHOR_INSTALL_TEST_FILES lets a pin
# be a local file:// URL; METAPHOR_INSTALL_TEST_UNSIGNED, only for such a pin, skips the signature check. A switch
# counts only with CI=true, as CI sets, so a player can't turn one on by accident (#408, #425).
function Test-TestSwitch([string] $Name) {
    $env:CI -eq 'true' -and [Environment]::GetEnvironmentVariable("METAPHOR_INSTALL_TEST_$Name") -eq '1'
}

# True when this process runs as administrator. Reloaded-II isn't meant to run as admin, and nothing but the two
# runtime installers needs it (they ask for it themselves), so install.ps1 refuses. The tests set
# METAPHOR_INSTALL_TEST_ALLOW_ADMIN, since CI's runners are admin.
function Test-Elevated {
    if (Test-TestSwitch 'ALLOW_ADMIN') { return $false }
    Test-AdministratorRole
}

function Test-AdministratorRole {
    ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# False only when EnableLUA is 0, which turns UAC off: every window of an administrator account then runs as
# administrator (#465). A missing or unreadable value counts as on, so the installer keeps refusing.
function Test-UacEnabled {
    $key = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' `
        -Name EnableLUA -ErrorAction SilentlyContinue
    if ($null -eq $key) { return $true }
    # Only a DWORD 0 counts, which Get-ItemProperty returns as Int32 (QWORD is Int64, a string String: pwsh and 5.1).
    return -not ($key.EnableLUA -is [int] -and $key.EnableLUA -eq 0)
}

# True when install.ps1 may go on in this window. An elevated window stops it, unless UAC is off, where no window is
# anything else: then SAY explains and ASKER (New-InstallAsker) takes a yes (#465). No answer, as without a console,
# counts as no.
function Confirm-RunWindow([scriptblock] $Say, $Asker) {
    if (-not (Test-Elevated)) { return $true }
    if (Test-UacEnabled) {
        & $Say 'This window runs as administrator, so the installer stopped without changing anything. Run it from a normal PowerShell window: it asks Windows for permission itself where a step needs it.'
        return $false
    }
    & $Say 'User Account Control is turned off on this computer, so every window runs as administrator, and the files the installer puts in place will be owned by the administrator account. Reloaded-II and the mod may then need administrator rights to update themselves later.'
    if (Confirm-Question $Asker 'Install as administrator anyway?') { return $true }
    & $Say 'The installer stopped without changing anything, because the answer was no.'
    return $false
}

function Get-StreamSha256([IO.Stream] $Stream) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { -join ($hash.ComputeHash($Stream) | ForEach-Object { $_.ToString('x2') }) } finally { $hash.Dispose() }
}

function Test-SamePath([string] $A, [string] $B) {
    [IO.Path]::GetFullPath($A).TrimEnd('\', '/') -eq [IO.Path]::GetFullPath($B).TrimEnd('\', '/')
}

# What install.ps1 passes every step, and what the steps find for the later ones (Launcher, Loader, ModsFolder,
# AppsFolder, GameExe). ASKER (New-InstallAsker) asks the questions; SAY takes a printed line and a spoken one.
function New-InstallContext([string] $ReleaseFolder, [string] $PinsPath, [string] $ConfigPath, [string] $ReloadedFolder,
    [string] $SteamRoot, [string] $Desktop, [string] $DotnetRoot, [string] $VcRuntimeKey, [string] $WorkFolder,
    $Asker, [scriptblock] $Say) {
    $modConfig = Read-Json (Join-Path $ReleaseFolder 'ModConfig.json')
    [pscustomobject]@{
        ReleaseFolder = $ReleaseFolder; Pins = (Read-Json $PinsPath); ConfigPath = $ConfigPath
        ReloadedFolder = $ReloadedFolder; SteamRoot = $SteamRoot; Desktop = $Desktop; DotnetRoot = $DotnetRoot
        VcRuntimeKey = $VcRuntimeKey; WorkFolder = $WorkFolder; Asker = $Asker; Say = $Say
        ModId = [string] $modConfig.ModId; ModName = [string] (Get-Property $modConfig 'ModName')
        ModVersion = [string] (Get-Property $modConfig 'ModVersion')
        Launcher = $null; Loader = $null; ModsFolder = $null; AppsFolder = $null; GameExe = $null; InstalledMod = $null
    }
}

# How long a download may wait, in seconds (#458). WAIT bounds the connection and the reply's start, then each wait for
# more data, so a proxy or captive portal that accepts the connection and goes quiet fails instead of hanging. TOTAL
# bounds the whole download, so one that trickles a little data at a time fails too; it is generous, so that a slow
# connection still finishes.
$DownloadTimeout = @{ WaitSeconds = 60; TotalSeconds = 1800 }

# Downloads URI to PATH through PATH.part, deleted on any failure, so a partial download is never kept under PATH.
# Throws when it fails or runs past $DownloadTimeout.
function Save-Download([Uri] $Uri, [string] $Path) {
    $part = "$Path.part"
    try {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $request = [Net.WebRequest]::Create($Uri)
        $request.Timeout = $DownloadTimeout.WaitSeconds * 1000
        if ($request -is [Net.HttpWebRequest]) { $request.ReadWriteTimeout = $DownloadTimeout.WaitSeconds * 1000 }
        $response = $request.GetResponse()
        try {
            $source = $response.GetResponseStream()
            $file = [IO.File]::Open($part, 'Create', 'Write', 'None')
            try {
                $buffer = New-Object byte[] 81920
                while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $file.Write($buffer, 0, $read)
                    if ($clock.Elapsed.TotalSeconds -gt $DownloadTimeout.TotalSeconds) {
                        throw "the download took longer than $($DownloadTimeout.TotalSeconds) seconds"
                    }
                }
            }
            finally { $file.Dispose() }
        }
        finally { $response.Dispose() }
        Move-Item -LiteralPath $part -Destination $Path -Force
    }
    catch {
        Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
        throw
    }
}

# Downloads PIN into the work folder and checks it: { Path; Stream } on success, else { Path = $null; Step }, a failed
# step naming what went wrong. STREAM holds the file open, readable by others but not writable or deletable, from
# before it is hashed until the caller disposes it once tar or the installer has returned, so the file that runs is the
# file that was checked. A pin with a signer must also carry a valid Authenticode signature with exactly that subject.
function Get-PinnedFile($Context, $Pin) {
    $name = [string] $Pin.fileName
    if ($name -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { throw "install-pins.json: '$name' is not a plain file name" }
    $target = Join-Path $Context.WorkFolder $name
    $uri = [Uri] $Pin.url
    try {
        if ($uri.IsFile -and $uri.IsUnc) { throw "install-pins.json: no network file URLs, not $($Pin.url)" }
        elseif ($uri.IsFile -and (Test-TestSwitch 'FILES')) { Copy-Item -LiteralPath $uri.LocalPath $target -Force }
        elseif ($uri.Scheme -ne 'https') { throw "install-pins.json: only https downloads, not $($Pin.url)" }
        else {
            # Windows PowerShell 5.1 may not offer TLS 1.2 by default, which GitHub requires; its progress bar slows a
            # download many times over.
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            Save-Download $uri $target
        }
    }
    catch {
        if ($_.Exception.Message -like 'install-pins.json:*') { throw }
        return [pscustomobject]@{ Path = $null; Step = (Failed "The download of $($Pin.name) did not finish." `
                    'The internet connection, or a proxy or firewall, probably stopped it. Check that this computer is online, then run the installer again. If it fails again, report it with the alpha feedback form.' `
                    "$($Pin.url): $($_.Exception.Message)") }
    }
    $stream = [IO.File]::Open($target, 'Open', 'Read', 'Read')
    # Disposed here on every way out but success, a throw included, which hands STREAM to the caller.
    $handedOver = $false
    try {
        $problem = $null
        $sha256 = Get-StreamSha256 $stream
        if ($sha256 -ne $Pin.sha256) { $problem = "SHA-256 $sha256, expected $($Pin.sha256)" }
        elseif ((Get-Property $Pin 'signer') -and -not ($uri.IsFile -and (Test-TestSwitch 'UNSIGNED'))) {
            $signature = $null
            try { $signature = Get-AuthenticodeSignature -LiteralPath $target } catch { $problem = "signature not checked: $($_.Exception.Message)" }
            if ($signature -and ([string] $signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or
                    $signature.SignerCertificate.Subject -cne $Pin.signer)) {
                $subject = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { 'none' }
                $problem = "signature $($signature.Status), signer $subject, expected $($Pin.signer)"
            }
        }
        if ($problem) {
            $stream.Dispose()
            Remove-Item -LiteralPath $target -Force
            return [pscustomobject]@{ Path = $null; Stream = $null; Step = (Failed "The downloaded $($Pin.name) is not the file this installer expects, so it was deleted unused." `
                        'Run the installer again later. If it happens again, report it with the alpha feedback form.' "$($Pin.url): $problem") }
        }
        $handedOver = $true
        [pscustomobject]@{ Path = $target; Stream = $stream; Step = $null }
    }
    finally { if (-not $handedOver) { $stream.Dispose() } }
}

# Unpacks ARCHIVE (zip or 7z) into DESTINATION with Windows' own tar.exe, by its full path. Without -P it refuses
# entries with .. or an absolute path (bsdtar's manual, in the raw source). True when it worked.
function Expand-PinnedArchive([string] $Archive, [string] $Destination) {
    $tar = Join-Path ([Environment]::SystemDirectory) 'tar.exe'
    if (-not (Test-Path -LiteralPath $tar -PathType Leaf)) { return $false }
    New-Item -ItemType Directory -Force $Destination | Out-Null
    $errors = Join-Path (Split-Path $Archive) 'tar-errors.txt'
    $process = Start-Process -FilePath $tar -ArgumentList "-x -f `"$Archive`" -C `"$Destination`"" -Wait -PassThru `
        -NoNewWindow -RedirectStandardError $errors
    return $process.ExitCode -eq 0
}

# Asks QUESTION, a yes/no one where Enter means no.
function Confirm-Install($Context, [string] $Question) { Confirm-Question $Context.Asker $Question }

# 2. Reloaded-II: the one Reloaded-II's settings name (Find-ReloadedLauncher, without the developer scripts'
# METAPHOR_RELOADED_EXE), or one this installer put in its folder before; else the pinned Release.zip, unpacked there
# after a yes. No admin rights needed.
function Install-Reloaded($Context) {
    $pin = $Context.Pins.reloaded
    $launcher = Find-ReloadedLauncher -ConfigPath $Context.ConfigPath -IgnoreEnvironment
    $ours = Join-Path $Context.ReloadedFolder 'Reloaded-II.exe'
    if (-not $launcher -and (Test-Path -LiteralPath $ours -PathType Leaf)) { $launcher = $ours }
    if ($launcher -and -not (Test-LauncherName $launcher)) {
        return Failed 'Reloaded-II''s records name a program that is not Reloaded-II.exe, so the installer did not use it.' `
            'Start Reloaded-II once from its own Reloaded-II.exe, then run the installer again.' "Program: $launcher"
    }
    if ($launcher) {
        $Context.Launcher = [IO.Path]::GetFullPath($launcher)
        return Unchanged 'Reloaded-II is already installed.' "Program: $($Context.Launcher)"
    }
    if ((Test-Path -LiteralPath $Context.ReloadedFolder) -and @(Get-ChildItem -LiteralPath $Context.ReloadedFolder -Force).Count -gt 0) {
        return Failed 'The folder this installer puts Reloaded-II in already holds other files, so it was left alone.' `
            'Empty or move that folder, or install Reloaded-II yourself and start it once, then run the installer again.' "Folder: $($Context.ReloadedFolder)"
    }
    if (-not (Confirm-Install $Context "Reloaded-II, which loads the mod into the game, is not installed. Download Reloaded-II $($pin.version) from its GitHub page and install it in your user folder? This does not need administrator rights.")) {
        return Failed 'Reloaded-II was not installed, because the answer was no.' `
            "Run the installer again and answer yes, or install Reloaded-II $($pin.version) yourself and start it once."
    }
    & $Context.Say "Downloading Reloaded-II $($pin.version) from $($pin.url)" "Downloading Reloaded-II."
    $file = Get-PinnedFile $Context $pin
    if (-not $file.Path) { return $file.Step }
    try { $expanded = Expand-PinnedArchive $file.Path $Context.ReloadedFolder } finally { $file.Stream.Dispose() }
    if (-not $expanded -or -not (Test-Path -LiteralPath $ours -PathType Leaf)) {
        Remove-Item -LiteralPath $Context.ReloadedFolder -Recurse -Force -ErrorAction SilentlyContinue
        return Failed 'Reloaded-II could not be unpacked.' 'Run the installer again. If it fails again, report it with the alpha feedback form.' `
            "Archive: $($file.Path)"
    }
    $Context.Launcher = [IO.Path]::GetFullPath($ours)
    Done "Reloaded-II $($pin.version) is installed." "Folder: $($Context.ReloadedFolder)"
}

# 3. Reloaded-II's settings file. One that exists is read, never rewritten. Without one (a Reloaded-II that has never
# started), it writes the three entries the mod and the install check need, with the folders Reloaded-II uses by
# default beside its program; Reloaded-II fills in the rest when it starts the game
# (docs/raw-sources/reloaded-ii-config-location-2026-11-13.md; spoken-installer-sources-2026-11-19.md).
function Initialize-ReloadedConfig($Context) {
    $result = $null
    if (Test-Path -LiteralPath $Context.ConfigPath -PathType Leaf) {
        try { $Context.Loader = Read-Json $Context.ConfigPath } catch { $Context.Loader = $null }
        if ($null -eq $Context.Loader) {
            return Failed 'Reloaded-II''s settings file could not be read.' `
                'Start Reloaded-II once, so it writes its settings file again, then run the installer again.' "Settings file: $($Context.ConfigPath)"
        }
        $result = Unchanged 'Reloaded-II''s settings are already in place.' "Settings file: $($Context.ConfigPath)"
    }
    else {
        $folder = Split-Path $Context.Launcher
        $Context.Loader = [pscustomobject]@{
            LauncherPath = $Context.Launcher
            ApplicationConfigDirectory = (Join-Path $folder 'Apps')
            ModConfigDirectory = (Join-Path $folder 'Mods')
        }
        Write-JsonFile $Context.ConfigPath $Context.Loader
        $result = Done 'Reloaded-II''s settings file is written, with its own folders for mods and games.' "Settings file: $($Context.ConfigPath)"
    }
    $Context.ModsFolder = [string] (Get-Property $Context.Loader 'ModConfigDirectory')
    $Context.AppsFolder = [string] (Get-Property $Context.Loader 'ApplicationConfigDirectory')
    if (-not $Context.ModsFolder -or -not $Context.AppsFolder) {
        return Failed 'Reloaded-II''s settings file does not name its folders for mods and games.' `
            'Start Reloaded-II once, so it writes its settings file again, then run the installer again.' "Settings file: $($Context.ConfigPath)"
    }
    foreach ($folder in $Context.ModsFolder, $Context.AppsFolder) { New-Item -ItemType Directory -Force $folder -ErrorAction Stop | Out-Null }
    $result
}

# The .NET runtimes under DOTNETROOT: each of FRAMEWORKS at MINIMUM or a later version with the same major, as
# dotnet --list-runtimes would list them.
function Test-DotnetRuntime([string] $DotnetRoot, [string[]] $Frameworks, [version] $Minimum) {
    foreach ($framework in $Frameworks) {
        $folder = Join-Path $DotnetRoot "shared\$framework"
        $found = @(Get-ChildItem -LiteralPath $folder -Directory -ErrorAction SilentlyContinue | Where-Object {
                $version = $null
                [version]::TryParse($_.Name, [ref] $version) -and $version.Major -eq $Minimum.Major -and $version -ge $Minimum
            })
        if ($found.Count -eq 0) { return $false }
    }
    $true
}

# The Visual C++ runtime, by the registry key Microsoft documents for it.
function Test-VcRuntime([string] $Key) {
    $values = Get-ItemProperty -LiteralPath $Key -ErrorAction SilentlyContinue
    [bool]($values -and $values.PSObject.Properties['Version'] -and $values.Version)
}

# 4 and 5. A runtime Reloaded-II needs: already there, or the pinned Microsoft installer, run quietly after a yes. Its
# own admin prompt follows; this script never runs as admin itself.
function Install-Runtime($Context, $Pin, [scriptblock] $Test) {
    if (& $Test) { return Unchanged "$($Pin.name) is already installed." }
    $question = "$($Pin.name), which Reloaded-II needs, is not installed. Download version $($Pin.version) from Microsoft and install it? This needs administrator rights: Windows will ask for permission, and you answer yes there too."
    if (-not (Confirm-Install $Context $question)) {
        return Failed "$($Pin.name) was not installed, because the answer was no." `
            "Run the installer again and answer yes, or install $($Pin.name) yourself from Microsoft's website."
    }
    & $Context.Say "Downloading $($Pin.name) $($Pin.version) from $($Pin.url)" "Downloading $($Pin.name)."
    $file = Get-PinnedFile $Context $Pin
    if (-not $file.Path) { return $file.Step }
    & $Context.Say "Installing $($Pin.name). Windows asks for administrator permission now: answer yes." `
        "Installing $($Pin.name). Windows asks for administrator permission now. Answer yes."
    try { $code = (Start-Process -FilePath $file.Path -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru -WindowStyle Hidden).ExitCode }
    catch { $code = $Cancelled }
    finally { $file.Stream.Dispose() }
    if ($code -eq $Cancelled) {
        return Failed "The install of $($Pin.name) was cancelled, maybe at Windows' permission prompt." `
            "Run the installer again, and answer yes at Windows' permission prompt."
    }
    if ($InstallerOk -notcontains $code -or -not (& $Test)) {
        return Failed "The install of $($Pin.name) did not work." `
            "Restart Windows and run the installer again. If it fails again, install $($Pin.name) yourself from Microsoft's website." "Exit code: $code"
    }
    if ($code -eq $RestartNeeded) { return Done "$($Pin.name) is installed. Windows asked for a restart to finish it: restart before you play." }
    Done "$($Pin.name) $($Pin.version) is installed."
}

# A closure (GetNewClosure) runs outside this module, so it can't call the module's own functions by name; each test
# is captured as a script block instead, which stays this module's without exporting it.
function Install-DotnetRuntime($Context) {
    $pin = $Context.Pins.dotnetDesktop
    $test = ${function:Test-DotnetRuntime}
    $check = { & $test $Context.DotnetRoot @($pin.frameworks) ([version] $pin.minimumVersion) }.GetNewClosure()
    Install-Runtime $Context $pin $check
}

function Install-VcRuntime($Context) {
    $key = $Context.VcRuntimeKey
    $test = ${function:Test-VcRuntime}
    Install-Runtime $Context $Context.Pins.vcRedist { & $test $key }.GetNewClosure()
}

# The folder in the mods folder whose ModConfig.json names MODID, or $null.
function Find-ModFolder([string] $ModsFolder, [string] $ModId) {
    Get-ChildItem -LiteralPath $ModsFolder -Directory -ErrorAction SilentlyContinue | Where-Object {
        $config = Join-Path $_.FullName 'ModConfig.json'
        try { (Test-Path -LiteralPath $config -PathType Leaf) -and [string] (Get-Property (Read-Json $config) 'ModId') -eq $ModId } catch { $false }
    } | Select-Object -First 1
}

# 6. Reloaded-II's hooks library, which the loader loads on every start and Reloaded-II's window would otherwise
# download: already in the mods folder, or the pinned release unpacked there after a yes.
function Install-HooksLibrary($Context) {
    $pin = $Context.Pins.hooksLibrary
    if (Find-ModFolder $Context.ModsFolder $pin.modId) { return Unchanged "$($pin.name) is already installed." }
    $target = Join-Path $Context.ModsFolder $pin.modId
    if (Test-Path -LiteralPath $target) {
        return Failed "A folder for $($pin.name) is in Reloaded-II's mods folder but is not that library, so it was left alone." `
            'Move that folder out of the mods folder, then run the installer again.' "Folder: $target"
    }
    if (-not (Confirm-Install $Context "Reloaded-II's hooks library, which the mod needs, is not installed. Download version $($pin.version) from its GitHub page?")) {
        return Failed "$($pin.name) was not installed, because the answer was no." `
            'Run the installer again and answer yes, or open Reloaded-II once: it downloads the library itself.'
    }
    & $Context.Say "Downloading $($pin.name) $($pin.version) from $($pin.url)" "Downloading $($pin.name)."
    $file = Get-PinnedFile $Context $pin
    if (-not $file.Path) { return $file.Step }
    try { $expanded = Expand-PinnedArchive $file.Path $target } finally { $file.Stream.Dispose() }
    if (-not $expanded -or -not (Find-ModFolder $Context.ModsFolder $pin.modId)) {
        Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue
        return Failed "Windows could not unpack $($pin.name)." `
            'Open Reloaded-II once: it downloads the library itself. Then run the installer again.' "Archive: $($file.Path)"
    }
    Done "$($pin.name) $($pin.version) is installed." "Folder: $target"
}

# The game's AppConfig.json file in Reloaded-II's applications folder, or $null (also when LOADER is $null).
function Find-GameApplication($Loader) {
    $apps = [string] (Get-Property $Loader 'ApplicationConfigDirectory')
    if (-not $apps -or -not (Test-Path -LiteralPath $apps -PathType Container)) { return $null }
    Find-MetaphorApplication $Loader
}

# 1. The game, first, so nothing is downloaded for a computer without it: the one added to Reloaded-II, else one found
# in Steam (Find-MetaphorGame), confirmed or picked, else METAPHOR.exe chosen in the Open dialog or typed in.
function Select-InstallGame($Context) {
    $loader = $null
    if (Test-Path -LiteralPath $Context.ConfigPath -PathType Leaf) { try { $loader = Read-Json $Context.ConfigPath } catch { $loader = $null } }
    $application = Find-GameApplication $loader
    if ($application) {
        $location = [string] (Get-Property (Read-Json $application.FullName) 'AppLocation')
        $registered = if ($location) { Resolve-AppLocation $application.FullName $location } else { $null }
        if ($registered -and (Test-Path -LiteralPath $registered -PathType Leaf)) {
            $Context.GameExe = $registered
            return Unchanged 'The game is already added to Reloaded-II.' "Game: $registered"
        }
    }
    $locate = @{ ConfigPath = $Context.ConfigPath }
    if ($Context.SteamRoot) { $locate.SteamRoot = $Context.SteamRoot }
    $found = @(Find-MetaphorGame @locate)
    $path = $null
    if ($found.Count -eq 1) {
        & $Context.Say "Found the game: $($found[0])" 'Found the game in Steam.'
        if (Confirm-Question $Context.Asker 'Use the game found in Steam?' -EnterMeansYes) { $path = $found[0] }
    }
    elseif ($found.Count -gt 1) {
        & $Context.Say 'Found the game in more than one place:' "Found the game in $($found.Count) places."
        for ($i = 0; $i -lt $found.Count; $i++) { & $Context.Say "  $($i + 1). $($found[$i])" "Number $($i + 1): $($found[$i])" }
        if ($Context.Asker.Dialogs) {
            # One Yes or No window per place, in turn, until a yes (#607).
            for ($i = 0; $i -lt $found.Count -and -not $path; $i++) {
                $question = "Use the game in place $($i + 1) of $($found.Count)? $($found[$i])"
                if (Confirm-Question $Context.Asker $question -EnterMeansYes) { $path = $found[$i] }
            }
        }
        else {
            $answer = Read-AskerAnswer $Context.Asker "Which one? Type its number. [1-$($found.Count)]"
            if ($answer -match '^\s*\d+\s*$' -and [int]$answer -ge 1 -and [int]$answer -le $found.Count) { $path = $found[[int]$answer - 1] }
        }
    }
    else {
        $typed = Select-AskerFile $Context.Asker 'Did not find the game in Steam. Choose METAPHOR.exe in the game''s install folder.' `
            'METAPHOR.exe' 'Did not find the game in Steam. Type the full path to METAPHOR.exe, or press Enter to stop:'
        if ($typed -and (Split-Path -Leaf $typed) -eq 'METAPHOR.exe' -and (Test-Path -LiteralPath $typed -PathType Leaf)) {
            $path = [IO.Path]::GetFullPath($typed)
        }
    }
    if (-not $path) {
        return Failed 'No game was chosen.' 'Run the installer again and choose METAPHOR.exe in the game''s install folder.'
    }
    $Context.GameExe = $path
    Done 'The game is found.' "Game: $path"
}

# The package's own files, as package-files.txt lists them (scripts/release/package-mod.sh writes it), relative paths with
# forward slashes; the list itself included. $null when the list is missing or names a path outside the folder, or a
# file that isn't there.
function Get-PackageFile([string] $Folder) {
    $list = Join-Path $Folder $PackageListName
    if (-not (Test-Path -LiteralPath $list -PathType Leaf)) { return $null }
    $files = @($PackageListName)
    foreach ($line in Get-Content -Encoding UTF8 -LiteralPath $list) {
        if (-not $line) { continue }
        if ($line -cnotmatch '^[A-Za-z0-9._-]+(/[A-Za-z0-9._-]+)*$' -or $line -match '(^|/)\.\.?(/|$)') { return $null }
        if (-not (Test-Path -LiteralPath (Join-Path $Folder $line) -PathType Leaf)) { return $null }
        $files += $line
    }
    $files
}

# 7. The mod's files: the package's own (Get-PackageFile), copied into the mods folder from the folder this runs from,
# each only when it is missing or differs. Other files in either folder (the mod's status file, say) are left alone.
function Install-ModFiles($Context) {
    $existing = Find-ModFolder $Context.ModsFolder $Context.ModId
    $target = if ($existing) { $existing.FullName } else { Join-Path $Context.ModsFolder $Context.ModId }
    $Context.InstalledMod = $target
    if (Test-SamePath $target $Context.ReleaseFolder) {
        return Unchanged 'The installer is running from the installed mod, so its files are already in place.' "Folder: $target"
    }
    $source = [IO.Path]::GetFullPath($Context.ReleaseFolder).TrimEnd('\', '/')
    $files = Get-PackageFile $source
    if (-not $files) {
        return Failed 'The mod''s folder is missing its list of files, or the list does not match the folder.' `
            'Unzip the release zip again, and run install.ps1 from the new folder.' "Folder: $source"
    }
    $copied = 0
    foreach ($relative in $files) {
        $from = Join-Path $source $relative
        $destination = Join-Path $target $relative
        if ((Test-Path -LiteralPath $destination -PathType Leaf) -and (Get-Sha256 $destination) -eq (Get-Sha256 $from)) { continue }
        # -ErrorAction Stop: a module doesn't take install.ps1's preference, so a refused copy would otherwise count as done.
        New-Item -ItemType Directory -Force (Split-Path $destination) -ErrorAction Stop | Out-Null
        Copy-Item -LiteralPath $from $destination -Force -ErrorAction Stop
        $copied++
    }
    if ($copied -eq 0) { return Unchanged "The mod's files are already up to date." "Folder: $target" }
    Done "Copied $copied of the mod's files into Reloaded-II's mods folder." "Folder: $target"
}

# 8. The game in Reloaded-II with the mod turned on: the AppConfig.json Reloaded-II has for the game, with the mod added
# to its EnabledMods and its other entries kept, or a new one (reloaded-ii-app-and-mod-config-2026-11-15.md).
function Enable-Mod($Context) {
    $application = Find-GameApplication $Context.Loader
    if ($application) {
        $path = $application.FullName
        $app = Read-Json $path
        $changes = @()
        $location = [string] (Get-Property $app 'AppLocation')
        if (-not $location -or -not (Test-SamePath (Resolve-AppLocation $path $location) $Context.GameExe)) {
            $app | Add-Member -NotePropertyName AppLocation -NotePropertyValue $Context.GameExe -Force
            $changes += 'pointed at the game'
        }
        $enabled = @(Get-Property $app 'EnabledMods' | Where-Object { $_ })
        if ($enabled -notcontains $Context.ModId) {
            $app | Add-Member -NotePropertyName EnabledMods -NotePropertyValue ([object[]] ($enabled + $Context.ModId)) -Force
            $changes += 'turned the mod on'
        }
        if ($changes.Count -eq 0) { return Unchanged 'The mod is already turned on for the game.' "Game settings: $path" }
        Write-JsonFile $path $app
        return Done "In Reloaded-II's settings for the game, this $($changes -join ' and ')." "Game settings: $path"
    }
    $path = Join-Path $Context.AppsFolder 'metaphor.exe\AppConfig.json'
    if (Test-Path -LiteralPath $path) {
        return Failed 'Reloaded-II has a settings file for another program where the game''s belongs, so it was left alone.' `
            'Move that folder out of Reloaded-II''s Apps folder, then run the installer again.' "File: $path"
    }
    Write-JsonFile $path ([pscustomobject]@{
            AppId = 'metaphor.exe'; AppName = 'METAPHOR'; AppLocation = $Context.GameExe; AppArguments = ''
            EnabledMods = [object[]] @($Context.ModId)
        })
    Done 'The game is added to Reloaded-II with the mod turned on.' "Game settings: $path"
}

# 9. A desktop shortcut that starts the game through Reloaded-II with its mods (--launch, as Reloaded-II's FAQ gives it:
# reloaded-ii-launch-and-logs-2026-11-13.md). Rewritten only when it points elsewhere.
function Install-Shortcut($Context) {
    $path = Join-Path $Context.Desktop $ShortcutName
    if (-not (Test-LauncherName $Context.Launcher)) { throw "the shortcut would start $($Context.Launcher), which is not Reloaded-II.exe" }
    $arguments = "--launch `"$($Context.GameExe)`""
    $folder = Split-Path $Context.Launcher
    $shell = New-Object -ComObject WScript.Shell
    try {
        $existed = Test-Path -LiteralPath $path -PathType Leaf
        $link = $shell.CreateShortcut($path)
        if ($existed -and $link.TargetPath -eq $Context.Launcher -and $link.Arguments -eq $arguments -and $link.WorkingDirectory -eq $folder) {
            return Unchanged "The desktop shortcut is already in place: $([IO.Path]::GetFileNameWithoutExtension($ShortcutName))." "Shortcut: $path"
        }
        $link.TargetPath = $Context.Launcher
        $link.Arguments = $arguments
        $link.WorkingDirectory = $folder
        $link.Description = 'Starts Metaphor: ReFantazio through Reloaded-II, with the accessibility mod.'
        New-Item -ItemType Directory -Force $Context.Desktop -ErrorAction Stop | Out-Null
        $link.Save()
    }
    finally { [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
    Done "The desktop shortcut is made: $([IO.Path]::GetFileNameWithoutExtension($ShortcutName))." "Shortcut: $path"
}

# A step for Get-InstallStep: Run calls STEP (a function of this module, so it can reach the module's own functions)
# with CONTEXT, and turns an error it stops with into a failed step (Get-ErrorStep), dropping whatever the step
# returned before it stopped, so the player hears one outcome.
function New-InstallStep([string] $Doing, [scriptblock] $Step, $Context, [switch] $Optional) {
    $errorStep = ${function:Get-ErrorStep}
    $run = {
        try { $result = & $Step $Context }
        catch { $result = & $errorStep $_ }
        $result
    }.GetNewClosure()
    @{ Doing = $Doing; Run = $run; Optional = $Optional.IsPresent }
}

# The installer's steps for CONTEXT (New-InstallContext), in the order install.ps1 runs them: each { Doing, said before
# it runs; Run, which returns the step's result and never throws; Optional, true when the install goes on after it
# fails }.
function Get-InstallStep($Context) {
    New-InstallStep 'Looking for the game.' ${function:Select-InstallGame} $Context
    New-InstallStep 'Checking for Reloaded-II, which loads the mod into the game.' ${function:Install-Reloaded} $Context
    New-InstallStep 'Checking Reloaded-II''s settings.' ${function:Initialize-ReloadedConfig} $Context
    New-InstallStep 'Checking for the .NET 9 Desktop Runtime.' ${function:Install-DotnetRuntime} $Context
    New-InstallStep 'Checking for the Visual C++ Redistributable.' ${function:Install-VcRuntime} $Context
    New-InstallStep 'Checking for Reloaded-II''s hooks library.' ${function:Install-HooksLibrary} $Context
    New-InstallStep 'Copying the mod into Reloaded-II.' ${function:Install-ModFiles} $Context
    New-InstallStep 'Turning the mod on for the game.' ${function:Enable-Mod} $Context
    # Optional: the mod works without the shortcut, so the install goes on when it can't be made (#466).
    New-InstallStep 'Making the desktop shortcut.' ${function:Install-Shortcut} $Context -Optional
}

# The interface, by who calls it. install.ps1: its questions (New-InstallAsker), the run-window check, the context and
# its steps (Get-InstallStep), then the check. check-install.ps1: speech, then the check. onboard.ps1 and setup-wsl.ps1:
# the questions and the locator. Both: the yes/no wording and answer.
Export-ModuleMember -Function Test-QuestionWindow, New-InstallAsker, Confirm-Question, Confirm-RunWindow, New-InstallContext, Get-InstallStep,
    Find-ScreenReader, Select-Speech, Start-Voice, Get-InstallCheck, Get-InstallSummary, Format-Check, Format-SpokenCheck,
    Test-Interactive, Read-Answer, Format-YesNoQuestion, Test-Yes, Confirm-Step, Select-MetaphorGame, Find-MetaphorGame, Find-ReloadedLauncher `
    -Variable ShortcutName, StatusFileName, WslUnavailableExitCode
# Developer-only: the Reloaded-II reading that scripts/dev/ReloadedDev.psm1 re-exports to the developer scripts. No
# player script calls these.
Export-ModuleMember -Function Read-Json, Resolve-AppLocation, Resolve-LoaderPaths, Get-GameBuild
