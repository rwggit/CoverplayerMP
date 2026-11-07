# Native Chromaprint

Dieses Verzeichnis enthält die aus dem offiziellen Chromaprint-Quellcode erzeugten nativen Bibliotheken. Das Skript verwendet standardmäßig den verfügbaren Release-Tag 1.6.2.

## Erzeugung

Windows x64:

```powershell
pwsh ./build/Build-Chromaprint.ps1 -Target windows-x64
```

Android arm64 benötigt ein installiertes Android-NDK:

```powershell
$env:ANDROID_NDK_ROOT = 'C:\Android\Sdk\ndk\<version>'
pwsh ./build/Build-Chromaprint.ps1 -Target android-arm64
```

Beim Build des MAUI-Android-Projekts wird dieses Skript automatisch ausgeführt. Die erzeugte
`native/chromaprint/android-arm64/libchromaprint.so` wird anschließend als Android-Native-Library
für `arm64-v8a` in das APK übernommen. Ist kein NDK installiert, bricht der Android-Build mit
einem entsprechenden Hinweis ab. Das NDK kann über den Android SDK Manager installiert werden;
alternativ kann `ANDROID_NDK_ROOT` gesetzt werden.

iOS und MacCatalyst müssen wegen Xcode und des statischen Linkings auf macOS gebaut werden:

```bash
pwsh ./build/Build-Chromaprint.ps1 -Target ios-arm64
pwsh ./build/Build-Chromaprint.ps1 -Target maccatalyst-arm64
```

Die Bibliotheken dürfen erst nach dem Build in die jeweiligen App-Pakete übernommen werden. `NativeChromaContext` benötigt `libchromaprint` zusätzlich zu den FFmpeg-Bibliotheken.

## Lizenz

Chromaprint steht unter der MIT-Lizenz. FFmpeg wird separat über LGPLv2-Runtime-Pakete bezogen und benötigt die zugehörigen Lizenz- und NOTICE-Dateien. Die Lizenztexte müssen mit jeder veröffentlichten Anwendung ausgeliefert werden.

Quelle: https://github.com/acoustid/chromaprint
