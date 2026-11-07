[CmdletBinding()]
param(
	[ValidateSet('windows-x64', 'android-arm64', 'ios-arm64', 'maccatalyst-arm64')]
	[string]$Target = 'windows-x64',
	[string]$Version = '1.6.2',
	[string]$OutputRoot,
	[string]$AndroidNdk,
	[string]$AndroidNdkVersion = '27.2.12479018',
	[switch]$Clean
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
if ($cmakeCommand) {
	$cmake = $cmakeCommand.Source
}
else {
	$cmake = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\*\*\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe" -ErrorAction SilentlyContinue |
		Select-Object -First 1 -ExpandProperty FullName
}
if ([string]::IsNullOrWhiteSpace($cmake) -or -not (Test-Path $cmake)) {
	throw 'CMake wurde nicht gefunden. Installieren Sie die Visual-Studio-Komponente CMake oder fügen Sie CMake dem PATH hinzu.'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
	$OutputRoot = Join-Path $scriptRoot '..\native\chromaprint'
}
$androidNdkCandidates = @($AndroidNdk, $env:ANDROID_NDK_ROOT, $env:ANDROID_NDK_HOME)
if ($env:ANDROID_HOME) { $androidNdkCandidates += Join-Path $env:ANDROID_HOME 'ndk' }
if ($env:ANDROID_SDK_ROOT) { $androidNdkCandidates += Join-Path $env:ANDROID_SDK_ROOT 'ndk' }
if ($env:LOCALAPPDATA) { $androidNdkCandidates += Join-Path $env:LOCALAPPDATA 'Android\Sdk\ndk' }
$androidNdkCandidates = $androidNdkCandidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$sourceRoot = Join-Path $scriptRoot ".cache\chromaprint-$Version"
$archive = Join-Path $scriptRoot ".cache\chromaprint-$Version.tar.gz"
$androidNdkCacheRoot = Join-Path $scriptRoot ".cache\android-ndk-$AndroidNdkVersion"
$sourceUrl = "https://github.com/acoustid/chromaprint/archive/refs/tags/v$Version.tar.gz"
$buildRoot = Join-Path $scriptRoot ".cache\build-$Target"
$installRoot = Join-Path $OutputRoot $Target

if ($Clean) {
	Remove-Item $sourceRoot, $buildRoot, $installRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($Target -eq 'android-arm64') {
	$cmakeCache = Join-Path $buildRoot 'CMakeCache.txt'
	if (Test-Path $cmakeCache) {
		$generator = (Select-String -Path $cmakeCache -Pattern '^CMAKE_GENERATOR:INTERNAL=' | Select-Object -First 1).Line
		if ($generator -ne 'CMAKE_GENERATOR:INTERNAL=Ninja') {
			Remove-Item $buildRoot -Recurse -Force
		}
	}
}

New-Item (Split-Path $archive), $OutputRoot -ItemType Directory -Force | Out-Null
if (-not (Test-Path $sourceRoot)) {
	if (-not (Test-Path $archive)) {
		Write-Host "Lade Chromaprint $Version ..."
		Invoke-WebRequest $sourceUrl -OutFile $archive
	}
	tar -xzf $archive -C (Split-Path $sourceRoot)
	$extracted = Join-Path (Split-Path $sourceRoot) "chromaprint-$Version"
	if ($extracted -ne $sourceRoot) {
		Move-Item $extracted $sourceRoot
	}
}

$cmakeArgs = @(
	"-S", $sourceRoot,
	"-B", $buildRoot,
	"-DCMAKE_BUILD_TYPE=Release",
	"-DBUILD_TOOLS=OFF",
	"-DBUILD_TESTS=OFF",
	"-DBUILD_SHARED_LIBS=ON",
	"-DFFT_LIB=kissfft",
	"-DKISSFFT_SOURCE_DIR=$(Join-Path $sourceRoot 'src\3rdparty\kissfft')",
	"-DCMAKE_INSTALL_PREFIX=$installRoot"
)

switch ($Target) {
	'windows-x64' {
		$cmakeArgs += @('-A', 'x64')
	}
	'android-arm64' {
		$AndroidNdk = $androidNdkCandidates |
			ForEach-Object {
				if (Test-Path (Join-Path $_ 'build\cmake\android.toolchain.cmake')) { $_; return }
				if (Test-Path $_) {
					Get-ChildItem $_ -Directory -ErrorAction SilentlyContinue |
						Sort-Object Name -Descending |
						Where-Object { Test-Path (Join-Path $_.FullName 'build\cmake\android.toolchain.cmake') } |
						Select-Object -First 1 -ExpandProperty FullName
				}
			} |
			Select-Object -First 1
		if ([string]::IsNullOrWhiteSpace($AndroidNdk)) {
			$ndkArchive = Join-Path $scriptRoot ".cache\android-ndk-$AndroidNdkVersion-windows.zip"
			$ndkUrl = "https://dl.google.com/android/repository/android-ndk-r27c-windows.zip"
			if (-not (Test-Path (Join-Path $androidNdkCacheRoot 'build\cmake\android.toolchain.cmake'))) {
				New-Item (Split-Path $ndkArchive), $androidNdkCacheRoot -ItemType Directory -Force | Out-Null
				$archiveIsValid = $false
				if (Test-Path $ndkArchive) {
					try {
						& tar -tf $ndkArchive *> $null
						$archiveIsValid = $LASTEXITCODE -eq 0
					}
					catch {
						$archiveIsValid = $false
					}
				}
				if (-not $archiveIsValid) {
					Remove-Item $ndkArchive -Force -ErrorAction SilentlyContinue
				}
				if (-not (Test-Path $ndkArchive)) {
					Write-Host "Lade Android-NDK $AndroidNdkVersion ..."
					$temporaryArchive = "$ndkArchive.download"
					Remove-Item $temporaryArchive -Force -ErrorAction SilentlyContinue
					Invoke-WebRequest $ndkUrl -OutFile $temporaryArchive
					Move-Item $temporaryArchive $ndkArchive -Force
				}
				$extractRoot = Join-Path $scriptRoot '.cache\android-ndk-extract'
				Remove-Item $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
				try {
					Expand-Archive $ndkArchive -DestinationPath $extractRoot -Force
				}
				catch {
					Remove-Item $ndkArchive -Force -ErrorAction SilentlyContinue
					throw "Das Android-NDK-Archiv ist beschädigt oder unvollständig. Bitte den Build erneut starten, damit es erneut geladen wird. Details: $($_.Exception.Message)"
				}
				$extractedNdk = Get-ChildItem $extractRoot -Directory | Select-Object -First 1
				if ($null -eq $extractedNdk) { throw "Das Android-NDK-Archiv '$ndkArchive' enthält kein NDK-Verzeichnis." }
				New-Item $androidNdkCacheRoot -ItemType Directory -Force | Out-Null
				Copy-Item (Join-Path $extractedNdk.FullName '*') $androidNdkCacheRoot -Recurse -Force
			}
			$AndroidNdk = $androidNdkCacheRoot
		}
		$cmakeArgs += @(
			'-G', 'Ninja',
			"-DCMAKE_TOOLCHAIN_FILE=$(Join-Path $AndroidNdk 'build\cmake\android.toolchain.cmake')",
			'-DANDROID_ABI=arm64-v8a',
			'-DANDROID_PLATFORM=android-24'
		)
	}
	'ios-arm64' {
		if (-not $IsMacOS) {
			throw 'iOS-Chromaprint muss auf macOS mit Xcode gebaut und statisch gelinkt werden.'
		}
		$cmakeArgs += @('-DCMAKE_SYSTEM_NAME=iOS', '-DCMAKE_OSX_ARCHITECTURES=arm64', '-DBUILD_SHARED_LIBS=OFF')
	}
	'maccatalyst-arm64' {
		if (-not $IsMacOS) {
			throw 'MacCatalyst-Chromaprint muss auf macOS mit Xcode gebaut werden.'
		}
		$cmakeArgs += @('-DCMAKE_SYSTEM_NAME=Darwin', '-DCMAKE_OSX_SYSROOT=macosx', '-DCMAKE_OSX_ARCHITECTURES=arm64')
	}
}

& $cmake @cmakeArgs
if ($LASTEXITCODE -ne 0) { throw "CMake-Konfiguration für Chromaprint fehlgeschlagen (Exitcode $LASTEXITCODE)." }

& $cmake --build $buildRoot --config Release --target chromaprint --parallel
if ($LASTEXITCODE -ne 0) { throw "Der Chromaprint-Build ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }

& $cmake --install $buildRoot --config Release
if ($LASTEXITCODE -ne 0) { throw "Die Chromaprint-Installation ist fehlgeschlagen (Exitcode $LASTEXITCODE)." }

$nativeFile = if ($Target -eq 'windows-x64') { 'chromaprint.dll' } elseif ($Target -eq 'maccatalyst-arm64') { 'libchromaprint.dylib' } else { 'libchromaprint.so' }
$installedNativeFile = Get-ChildItem $installRoot -Recurse -File -Filter $nativeFile | Select-Object -First 1
if (-not $installedNativeFile) {
	throw "Chromaprint wurde gebaut, aber '$nativeFile' wurde unter '$installRoot' nicht gefunden."
}
$nativeOutputFile = Join-Path $installRoot $nativeFile
if ([IO.Path]::GetFullPath($installedNativeFile.FullName) -ne [IO.Path]::GetFullPath($nativeOutputFile)) {
	Copy-Item $installedNativeFile.FullName $nativeOutputFile -Force
}

Write-Host "Chromaprint wurde nach '$installRoot' installiert."
