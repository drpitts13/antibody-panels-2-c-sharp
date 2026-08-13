param(
    [string]$RepoRoot = $PSScriptRoot
)

Add-Type -AssemblyName System.Drawing

$pngPath  = Join-Path $RepoRoot "AntibodyPanels\blood_bag_icon.png"
$icoPath  = Join-Path $RepoRoot "AntibodyPanels\app.ico"
$exePath  = Join-Path $RepoRoot "AntibodyPanels\bin\Release\net8.0-windows\AntibodyPanels.exe"
$desktopPath = [System.Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktopPath "Antibody Panel Management System.lnk"

if (-not (Test-Path $pngPath)) {
    throw "Blood bag icon not found: $pngPath"
}
if (-not (Test-Path $exePath)) {
    Write-Host "Building application..."
    dotnet build (Join-Path $RepoRoot "AntibodyPanels\AntibodyPanels.csproj") -c Release | Out-Host
    if (-not (Test-Path $exePath)) {
        throw "Executable not found after build: $exePath"
    }
}

# ── Build multi-size ICO ─────────────────────────────────────────────────────
$src = [System.Drawing.Bitmap]::FromFile($pngPath)
$sizes = @(256, 128, 64, 48, 32, 16)

$bitmapBytes = foreach ($sz in $sizes) {
    $resized = New-Object System.Drawing.Bitmap($src, $sz, $sz)
    $ms = New-Object System.IO.MemoryStream
    $resized.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $resized.Dispose()
    ,$ms.ToArray()   # comma forces array element (not unwrap)
    $ms.Dispose()
}

$fs  = [System.IO.File]::Create($icoPath)
$bw  = New-Object System.IO.BinaryWriter($fs)

# ICO header
$bw.Write([int16]0)                    # Reserved
$bw.Write([int16]1)                    # Type: ICO
$bw.Write([int16]$sizes.Count)         # Image count

# Directory entries
$offset = 6 + $sizes.Count * 16
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$s)                # Width  (0 = 256)
    $bw.Write([byte]$s)                # Height (0 = 256)
    $bw.Write([byte]0)                 # Palette colours
    $bw.Write([byte]0)                 # Reserved
    $bw.Write([int16]1)                # Planes
    $bw.Write([int16]32)               # Bits per pixel
    $bw.Write([int32]$bitmapBytes[$i].Length)
    $bw.Write([int32]$offset)
    $offset += $bitmapBytes[$i].Length
}
foreach ($b in $bitmapBytes) { $bw.Write($b) }
$bw.Flush()
$fs.Close()
$src.Dispose()
Write-Host "ICO written: $icoPath"

Write-Host "Rebuilding with embedded icon..."
dotnet build (Join-Path $RepoRoot "AntibodyPanels\AntibodyPanels.csproj") -c Release | Out-Host

# ── Create desktop shortcut ──────────────────────────────────────────────────
$wsh = New-Object -ComObject WScript.Shell
$lnk = $wsh.CreateShortcut($shortcutPath)
$lnk.TargetPath       = $exePath
$lnk.WorkingDirectory = (Split-Path $exePath)
$lnk.IconLocation     = "$icoPath,0"
$lnk.Description      = "Antibody Panel Management System"
$lnk.WindowStyle      = 1   # Normal window
$lnk.Save()
Write-Host "Shortcut created: $shortcutPath"
