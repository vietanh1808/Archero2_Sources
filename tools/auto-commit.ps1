<#
.SYNOPSIS
    Tu dong commit tat ca file chua commit (untracked + modified + deleted) cua mot git repo.

.DESCRIPTION
    - Tu xu ly .git/index.lock bi ket (do Fork / SourceTree / GitKraken / git crash de lai).
    - Stage theo batch nen chay duoc voi repo hang chuc nghin file (vd: Unity decompiled sources).
    - Ton trong .gitignore (dung `git add`, khong ep buoc).
    - Co che do -Watch de commit dinh ky.

.PARAMETER RepoPath
    Duong dan repo. Mac dinh: thu muc hien tai.

.PARAMETER Message
    Commit message. Mac dinh: "Auto commit: <N> file(s) - <timestamp>".

.PARAMETER BatchSize
    So file stage moi lan goi `git add`. 0 = stage mot lan (nhanh nhat). Dung > 0 neu bi treo.

.PARAMETER Push
    Push len remote sau khi commit thanh cong.

.PARAMETER Watch
    Chay lien tuc, commit moi -IntervalSeconds giay. Ctrl+C de dung.

.PARAMETER IntervalSeconds
    Chu ky cho che do -Watch. Mac dinh 300 (5 phut).

.PARAMETER KillLockingApps
    Neu lock bi giu ket, tu dong tat cac git GUI dang chay (Fork, SourceTree, GitKraken, TortoiseGit).
    KHONG bat mac dinh vi se lam mat thao tac dang do tren GUI.

.PARAMETER DryRun
    Chi in ra se lam gi, khong stage/commit.

.EXAMPLE
    .\tools\auto-commit.ps1
    Commit tat ca thay doi trong repo hien tai.

.EXAMPLE
    .\tools\auto-commit.ps1 -Message "Import Archero sources" -Push

.EXAMPLE
    .\tools\auto-commit.ps1 -Watch -IntervalSeconds 600
    Cu 10 phut tu commit mot lan.
#>
[CmdletBinding()]
param(
    [string]$RepoPath = (Get-Location).Path,
    [string]$Message,
    [int]$BatchSize = 0,
    [switch]$Push,
    [switch]$Watch,
    [int]$IntervalSeconds = 300,
    [switch]$KillLockingApps,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$GuiClients = @('Fork', 'SourceTree', 'gitkraken', 'TortoiseGitProc', 'TortoiseProc', 'GitHubDesktop')

function Write-Step { param([string]$Text) Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Text) Write-Host "    $Text" -ForegroundColor Green }
function Write-Warn { param([string]$Text) Write-Host "    $Text" -ForegroundColor Yellow }

# Ghep argv theo quy tac CommandLineToArgvW cua Windows.
# ProcessStartInfo.ArgumentList khong ton tai tren .NET Framework (Windows PowerShell 5.1),
# nen phai tu build chuoi Arguments.
function ConvertTo-WindowsArgLine {
    param([string[]]$Items)

    $parts = foreach ($item in $Items) {
        if ($item.Length -gt 0 -and $item -notmatch '[\s"]') {
            $item
        }
        else {
            $sb = New-Object System.Text.StringBuilder
            [void]$sb.Append('"')
            $slashes = 0
            foreach ($ch in $item.ToCharArray()) {
                if ($ch -eq '\') { $slashes++; continue }
                if ($ch -eq '"') {
                    # Backslash truoc dau " phai duoc nhan doi, roi escape chinh dau ".
                    [void]$sb.Append('\' * ($slashes * 2 + 1)).Append('"')
                }
                else {
                    [void]$sb.Append('\' * $slashes).Append($ch)
                }
                $slashes = 0
            }
            # Backslash cuoi chuoi dung truoc dau " dong => nhan doi.
            [void]$sb.Append('\' * ($slashes * 2)).Append('"')
            $sb.ToString()
        }
    }
    return ($parts -join ' ')
}

# Goi git va tra ve @{ Ok; Out; Err; Code }.
# Khong dung "2>&1" vi PS 5.1 bien stderr cua native exe thanh NativeCommandError gia.
function Invoke-Git {
    param([string[]]$GitArgs)

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName               = 'git'
    $psi.Arguments              = ConvertTo-WindowsArgLine -Items (@('-C', $RepoPath) + $GitArgs)
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError  = $true
    $psi.UseShellExecute        = $false
    $psi.CreateNoWindow         = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding  = [System.Text.Encoding]::UTF8
    # Khong redirect stdin: .NET Framework dat StandardInput.AutoFlush = true ngay trong
    # Process.Start, lam StreamWriter day BOM cua Console.InputEncoding vao dau stdin
    # truoc khi minh kip ghi => git doc pathspec dau tien bi dinh BOM. Dung file tam thay the.

    $proc = [System.Diagnostics.Process]::Start($psi)
    # Doc het output truoc khi WaitForExit de tranh deadlock khi output lon.
    $out = $proc.StandardOutput.ReadToEnd()
    $err = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()

    [pscustomobject]@{
        Ok   = ($proc.ExitCode -eq 0)
        Out  = $out
        Err  = $err
        Code = $proc.ExitCode
    }
}

# Co tien trinh nao dang mo file lock khong? Mo exclusive (FileShare.None):
# thanh cong => khong ai giu => lock chet. IOException => dang bi giu that.
function Test-Lock-IsHeld {
    param([string]$LockPath)

    if (-not (Test-Path -LiteralPath $LockPath)) { return $false }
    try {
        $fs = [System.IO.File]::Open(
            $LockPath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        $fs.Close()
        $fs.Dispose()
        return $false
    }
    catch [System.IO.IOException] { return $true }
    catch [System.UnauthorizedAccessException] { return $true }
    catch { return $true }
}

# Xu ly .git/index.lock: cho lock tam thoi tu nha, chi go khi that su la lock chet.
function Resolve-IndexLock {
    param([string]$GitDir, [int]$WaitSeconds = 30)

    $lock = Join-Path $GitDir 'index.lock'
    if (-not (Test-Path -LiteralPath $lock)) { return $true }

    Write-Warn "Phat hien index.lock, cho toi $WaitSeconds giay xem co tu nha khong..."
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if (-not (Test-Path -LiteralPath $lock)) {
            Write-Ok "Lock da duoc nha."
            return $true
        }
    }

    # Van con lock. Cau hoi dung khong phai "co app git nao dang chay khong" (Fork/GitKraken
    # dung libgit2 in-process, khong sinh git.exe; va mot app mo repo KHAC thi vo can),
    # ma la "co tien trinh nao dang GIU file lock nay khong". Git/libgit2 giu fd mo suot
    # thoi gian ghi index => neu minh mo duoc exclusive thi lock la lock chet.
    if (Test-Lock-IsHeld -LockPath $lock) {
        $suspects = @(Get-Process -Name (@('git') + $GuiClients) -ErrorAction SilentlyContinue)
        $names = if ($suspects.Count -gt 0) {
            ($suspects | ForEach-Object { "$($_.ProcessName) (PID $($_.Id))" }) -join ', '
        } else { '(khong ro tien trinh nao)' }

        if ($KillLockingApps -and $suspects.Count -gt 0) {
            Write-Warn "Lock dang bi giu. Dang tat: $names"
            foreach ($s in $suspects) {
                try { Stop-Process -Id $s.Id -Force -ErrorAction Stop } catch {}
            }
            Start-Sleep -Seconds 2
        }
        else {
            Write-Warn "index.lock dang bi mot tien trinh giu. Nghi ngo: $names"
            Write-Warn "Dong chung lai, hoac chay lai voi -KillLockingApps."
            return $false
        }
    }

    if (Test-Path -LiteralPath $lock) {
        if (Test-Lock-IsHeld -LockPath $lock) {
            Write-Warn "Lock van bi giu sau khi da thu go. Bo qua."
            return $false
        }
        Write-Warn "Go lock chet (khong tien trinh nao giu): $lock"
        if (-not $DryRun) { Remove-Item -LiteralPath $lock -Force }
    }
    return $true
}

# Doc danh sach duong dan thay doi tu "git status --porcelain -z"
# (NUL-separated => an toan voi ten file co dau, space, ky tu la).
function Get-ChangedPaths {
    $res = Invoke-Git -GitArgs @('status', '--porcelain', '-z')
    if (-not $res.Ok) { throw "git status that bai: $($res.Err.Trim())" }
    if ([string]::IsNullOrEmpty($res.Out)) { return @() }

    $paths  = New-Object System.Collections.Generic.List[string]
    $fields = $res.Out -split "`0"
    for ($i = 0; $i -lt $fields.Count; $i++) {
        $f = $fields[$i]
        if ([string]::IsNullOrEmpty($f)) { continue }
        if ($f.Length -lt 4) { continue }
        $xy   = $f.Substring(0, 2)
        $path = $f.Substring(3)
        # Rename/copy: ban ghi ke tiep la duong dan cu => lay ca hai.
        if ($xy[0] -eq 'R' -or $xy[0] -eq 'C') {
            $i++
            if ($i -lt $fields.Count -and -not [string]::IsNullOrEmpty($fields[$i])) {
                $paths.Add($fields[$i])
            }
        }
        $paths.Add($path)
    }
    return $paths.ToArray()
}

function Invoke-AutoCommit {
    Write-Step "Repo: $RepoPath"

    $top = Invoke-Git -GitArgs @('rev-parse', '--show-toplevel')
    if (-not $top.Ok) { throw "Khong phai git repository: $RepoPath" }
    $gitDir = (Invoke-Git -GitArgs @('rev-parse', '--absolute-git-dir')).Out.Trim()

    if (-not (Resolve-IndexLock -GitDir $gitDir)) {
        Write-Warn "Bo qua lan nay vi index dang bi khoa."
        return $false
    }

    # "branch --show-current" tra ve dung ten branch ke ca khi chua co commit nao
    # (rev-parse --abbrev-ref HEAD se tra ve chuoi "HEAD" trong truong hop do).
    $branch = (Invoke-Git -GitArgs @('branch', '--show-current')).Out.Trim()
    if ([string]::IsNullOrWhiteSpace($branch)) { $branch = '(detached HEAD)' }
    Write-Step "Branch: $branch"

    Write-Step "Dang quet thay doi..."
    $changed = Get-ChangedPaths
    if ($changed.Count -eq 0) {
        Write-Ok "Khong co gi de commit. Working tree sach."
        return $true
    }
    Write-Ok "Tim thay $($changed.Count) duong dan thay doi."

    if ($DryRun) {
        Write-Step "DryRun - 20 duong dan dau:"
        $changed | Select-Object -First 20 | ForEach-Object { Write-Host "      $_" }
        if ($changed.Count -gt 20) { Write-Host "      ... va $($changed.Count - 20) muc khac" }
        return $true
    }

    Write-Step "Dang stage..."
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    if ($BatchSize -le 0) {
        $add = Invoke-Git -GitArgs @('add', '-A')
        if (-not $add.Ok) { throw "git add that bai: $($add.Err.Trim())" }
    }
    else {
        $specFile = [System.IO.Path]::GetTempFileName()
        # UTF-8 KHONG BOM: git doc pathspec file theo bytes, BOM se lam hong duong dan dau tien.
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        try {
            for ($i = 0; $i -lt $changed.Count; $i += $BatchSize) {
                $end   = [Math]::Min($i + $BatchSize, $changed.Count) - 1
                $chunk = @($changed[$i..$end])
                # NUL-separated qua file tam: khong dinh gioi han do dai command line,
                # va an toan voi moi ky tu hop le trong ten file.
                $payload = ($chunk -join "`0") + "`0"
                [System.IO.File]::WriteAllBytes($specFile, $utf8NoBom.GetBytes($payload))

                $add = Invoke-Git -GitArgs @('add', "--pathspec-from-file=$specFile", '--pathspec-file-nul')
                if (-not $add.Ok) { throw "git add batch that bai: $($add.Err.Trim())" }
                Write-Host "    staged $($end + 1)/$($changed.Count)"
            }
        }
        finally {
            Remove-Item -LiteralPath $specFile -Force -ErrorAction SilentlyContinue
        }
    }
    $sw.Stop()
    Write-Ok "Stage xong sau $([math]::Round($sw.Elapsed.TotalSeconds, 1))s."

    # Co gi thuc su duoc stage khong? (.gitignore co the da loai het)
    $staged = Invoke-Git -GitArgs @('diff', '--cached', '--name-only')
    if ([string]::IsNullOrWhiteSpace($staged.Out)) {
        Write-Ok "Khong co file nao duoc stage (co the bi .gitignore loai). Bo qua commit."
        return $true
    }
    $stagedCount = @($staged.Out -split "`n" | Where-Object { $_.Trim() }).Count

    $msg = $Message
    if ([string]::IsNullOrWhiteSpace($msg)) {
        $msg = "Auto commit: $stagedCount file(s) - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    }

    Write-Step "Dang commit $stagedCount file..."
    $commit = Invoke-Git -GitArgs @('commit', '-m', $msg)
    if (-not $commit.Ok) {
        throw "git commit that bai: $($commit.Err.Trim())`n$($commit.Out.Trim())"
    }
    $sha = (Invoke-Git -GitArgs @('rev-parse', '--short', 'HEAD')).Out.Trim()
    Write-Ok "Da commit $sha - $msg"

    if ($Push) {
        if ($branch -eq '(detached HEAD)') {
            Write-Warn "Dang o detached HEAD, khong xac dinh duoc branch de push. Bo qua push."
            return $false
        }
        Write-Step "Dang push len origin/$branch..."
        $p = Invoke-Git -GitArgs @('push', 'origin', $branch)
        if (-not $p.Ok) {
            Write-Warn "Push that bai: $($p.Err.Trim())"
            return $false
        }
        Write-Ok "Push thanh cong."
    }
    return $true
}

if ($Watch) {
    Write-Step "Che do watch: commit moi $IntervalSeconds giay. Ctrl+C de dung."
    while ($true) {
        try { $null = Invoke-AutoCommit }
        catch { Write-Warn "Loi: $($_.Exception.Message)" }
        Write-Host ""
        Start-Sleep -Seconds $IntervalSeconds
    }
}
else {
    $ok = Invoke-AutoCommit
    if (-not $ok) { exit 1 }
}
