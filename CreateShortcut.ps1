#Requires -Version 5.1
<#
.SYNOPSIS
    Builds AntibodyPanels and creates a desktop shortcut with the blood bag icon.
.DESCRIPTION
    1. Builds the project in Release mode via dotnet build.
    2. Converts blood_bag_icon.png into a proper multi-size .ico file.
    3. Places the .ico next to the executable.
    4. Creates (or updates) a shortcut on the current user's desktop.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ProjectRoot = $PSScriptRoot
$CsprojPath  = Join-Path $ProjectRoot 'AntibodyPanels\AntibodyPanels.csproj'
$PngPath     = Join-Path $ProjectRoot 'AntibodyPanels\blood_bag_icon.png'
$ExePath     = Join-Path $ProjectRoot 'AntibodyPanels\bin\Release\net8.0-windows\AntibodyPanels.exe'
$IcoPath     = Join-Path $ProjectRoot 'AntibodyPanels\bin\Release\net8.0-windows\blood_bag.ico'
$ShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Antibody Panels.lnk'

# ---------------------------------------------------------------------------
# 1. Build
# ---------------------------------------------------------------------------
Write-Host "`n==> Building AntibodyPanels (Release)..." -ForegroundColor Cyan
dotnet build $CsprojPath --configuration Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }
Write-Host "    Build succeeded." -ForegroundColor Green

# ---------------------------------------------------------------------------
# 2. Convert PNG -> ICO (multi-size: 16, 32, 48, 256)
# ---------------------------------------------------------------------------
Write-Host "`n==> Converting blood_bag_icon.png to .ico ..." -ForegroundColor Cyan

Add-Type -AssemblyName System.Drawing

function ConvertPngToIco {
    param(
        [string]$Source,
        [string]$Destination,
        [int[]]$Sizes = @(16, 32, 48, 256)
    )

    $original = [System.Drawing.Bitmap]::new($Source)

    # Build PNG-compressed image blobs for each size
    $blobs = @()
    foreach ($sz in $Sizes) {
        $bmp = [System.Drawing.Bitmap]::new($sz, $sz)
        $g   = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($original, 0, 0, $sz, $sz)
        $g.Dispose()

        $ms = [System.IO.MemoryStream]::new()
        if ($sz -ge 256) {
            # Modern ICO: embed PNG directly for 256+
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        } else {
            # Classic ICO: embed as 32-bpp BMP (DIB format)
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        $blobs += ,$ms.ToArray()
        $ms.Dispose()
        $bmp.Dispose()
    }
    $original.Dispose()

    # Write ICO container
    # Layout: 6-byte ICONDIR + (16-byte ICONDIRENTRY * N) + image blobs
    $headerSize = 6
    $entrySize  = 16
    $dataOffset = $headerSize + $entrySize * $Sizes.Count

    $stream = [System.IO.File]::OpenWrite($Destination)
    $w = [System.IO.BinaryWriter]::new($stream)

    # ICONDIR
    $w.Write([uint16]0)              # Reserved
    $w.Write([uint16]1)              # Type: 1 = ICO
    $w.Write([uint16]$Sizes.Count)  # Image count

    # ICONDIRENTRY[] — write entries first, then blobs
    $offset = [uint32]$dataOffset
    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $sz   = [int]$Sizes[$i]
        $bLen = [uint32]$blobs[$i].Length
        $w.Write([byte]$(if ($sz -ge 256) { 0 } else { $sz }))  # Width  (0 = 256)
        $w.Write([byte]$(if ($sz -ge 256) { 0 } else { $sz }))  # Height (0 = 256)
        $w.Write([byte]0)                # ColorCount
        $w.Write([byte]0)                # Reserved
        $w.Write([uint16]1)              # Planes
        $w.Write([uint16]32)             # BitCount
        $w.Write([uint32]$bLen)          # SizeInBytes
        $w.Write([uint32]$offset)        # Offset
        $offset += $bLen
    }

    # Image data
    foreach ($blob in $blobs) { $w.Write($blob) }

    $w.Close()
    $stream.Close()
}

ConvertPngToIco -Source $PngPath -Destination $IcoPath -Sizes @(16, 32, 48, 256)
Write-Host "    Icon written to: $IcoPath" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 3. Create / update the desktop shortcut
# ---------------------------------------------------------------------------
Write-Host "`n==> Creating desktop shortcut: $ShortcutPath ..." -ForegroundColor Cyan

$WshShell  = New-Object -ComObject WScript.Shell
$shortcut  = $WshShell.CreateShortcut($ShortcutPath)
$shortcut.TargetPath       = $ExePath
$shortcut.WorkingDirectory = Split-Path $ExePath
$shortcut.IconLocation     = "$IcoPath,0"
$shortcut.Description      = 'Antibody Panels'
$shortcut.Save()

Write-Host "    Shortcut created." -ForegroundColor Green
Write-Host "`nDone! Double-click 'Antibody Panels' on your desktop to launch the app.`n" -ForegroundColor Yellow
