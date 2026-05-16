param()

Add-Type -AssemblyName System.Drawing

$pngPath = "c:\Mac\Home\Documents\antibody-panels-2-c-sharp\AntibodyPanels\blood_bag_icon.png"
$icoPath = "c:\Mac\Home\Documents\antibody-panels-2-c-sharp\AntibodyPanels\app.ico"
$exePath = "c:\Mac\Home\Documents\antibody-panels-2-c-sharp\AntibodyPanels\bin\Release\net8.0-windows\AntibodyPanels.exe"
$desktopPath = [System.Environment]::GetFolderPath('Desktop')
$shortcutPath = Join-Path $desktopPath "Antibody Panel Management System.lnk"

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

# ── Create desktop shortcut ──────────────────────────────────────────────────
$wsh = New-Object -ComObject WScript.Shell
$lnk = $wsh.CreateShortcut($shortcutPath)
$lnk.TargetPath       = $exePath
$lnk.WorkingDirectory = (Split-Path $exePath)
$lnk.IconLocation     = "$exePath,0"
$lnk.Description      = "Antibody Panel Management System"
$lnk.WindowStyle      = 1   # Normal window
$lnk.Save()
Write-Host "Shortcut created: $shortcutPath"
