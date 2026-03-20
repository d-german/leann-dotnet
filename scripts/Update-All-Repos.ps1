# Update-All-Repos.ps1
# Pulls latest from default branch for each repo on the Z: drive
# Run after setup-git-repos.ps1 has initialized each folder

param(
    [string]$BaseDir = "Z:\"
)

$repos = Get-ChildItem $BaseDir -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName ".git") }

Write-Host "`nFound $($repos.Count) git repos in $BaseDir`n" -ForegroundColor Cyan

foreach ($repo in $repos) {
    $name = $repo.Name
    Write-Host "[$name] " -NoNewline -ForegroundColor White

    try {
        $branch = git -C $repo.FullName rev-parse --abbrev-ref HEAD 2>&1
        git -C $repo.FullName pull --ff-only origin $branch 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "OK ($branch)" -ForegroundColor Green
        } else {
            Write-Host "FAILED (exit $LASTEXITCODE)" -ForegroundColor Red
        }
    } catch {
        Write-Host "ERROR: $_" -ForegroundColor Red
    }
}

Write-Host "`nDone." -ForegroundColor Cyan
