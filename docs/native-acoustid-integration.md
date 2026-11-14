# Native AcoustID-/FFmpeg-Integration

## Aktueller Pfad

`NativeFirstAudioFingerprintProvider` ist in MAUI und Blazor Web registriert.

1. Wenn eine native Chromaprint-Bibliothek verfügbar ist, wird `FfmpegAudioFingerprintProvider` verwendet.
2. Dieser dekodiert die Audiodatei mit FFmpeg über `avformat`, `avcodec` und `swr_convert` in Mono-PCM mit 11025 Hz und 16 Bit.
3. Die PCM-Daten werden an `AcoustID.Native.NativeChromaContext` übergeben.
4. Fehlt die native Bibliothek oder ist sie inkompatibel, wird automatisch `FpcalcFingerprintProvider` verwendet.

## Paketierung

`Openur.ChromaprintStatic` Version `1.6.2.10` ist in den MAUI- und Web-Projekten eingebunden. Das Paket liefert native `fpcalc`-Executables für mehrere RIDs, einschließlich `win-x64`; es liefert keine `chromaprint.dll`.

Der Fpcalc-Provider sucht nach der Executable im Anwendungsverzeichnis und unter `runtimes/<RID>/native`.

## Validierung

- Gesamtlösung: Build erfolgreich.
- WAV-Integrationstest: erfolgreich.
- Testsignal: 3 Sekunden, Mono, 44,1 kHz.
- Ergebnis: Dauer 3 Sekunden, gültiger Fingerprint mit Länge 16.
- Die Validierung lief wegen der fehlenden `chromaprint.dll` über den paketierten `fpcalc`-Fallback.

## Bekannte Grenze

Für den direkten FFmpeg-/`NativeChromaContext`-Pfad muss eine ABI-kompatible native Chromaprint-Bibliothek je Zielplattform bereitgestellt werden:

- Windows: `chromaprint.dll` für `win-x64` bzw. `win-arm64`.
- Linux/Android: passende `libchromaprint.so`.
- macOS/MacCatalyst: passende `libchromaprint.dylib` oder Frameworks.
- iOS: eine kompatible statische oder frameworkbasierte Integration.

Eine `fpcalc.exe` kann nicht als Ersatz für `chromaprint.dll` geladen werden. Solange diese Bibliotheken nicht paketiert sind, bleibt `fpcalc` der tatsächlich verwendete und validierte Pfad.
