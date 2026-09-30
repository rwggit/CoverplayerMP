window.coverPlayer = (() => {
    const audio = new Audio();
    const pickPlaylist = () => new Promise(resolve => {
        const input = document.createElement("input");
        input.type = "file";
        input.accept = ".m3u,.m3u8,audio/x-mpegurl";
        input.onchange = async () => resolve(input.files.length ? await input.files[0].text() : null);
        input.click();
    });
    const readDuration = url => new Promise(resolve => {
        const probe = new Audio();
        probe.preload = "metadata";
        probe.onloadedmetadata = () => resolve(isFinite(probe.duration) ? probe.duration : 0);
        probe.onerror = () => resolve(0);
        probe.src = url;
    });
    const pickAudioLibrary = () => new Promise(resolve => {
        const input = document.createElement("input");
        input.type = "file";
        input.multiple = true;
        input.webkitdirectory = true;
        input.onchange = async () => {
            const files = [...input.files].filter(f => /\.(mp3|wav|m4a|flac|aac|ogg|opus)$/i.test(f.name));
            const result = [];
            for (const file of files) {
                const url = URL.createObjectURL(file);
                const name = file.name.replace(/\.[^.]+$/, "");
                const separator = name.indexOf(" - ");
                result.push({
                    source: url,
                    artist: separator > 0 ? name.substring(0, separator).trim() : "Unbekannter Interpret",
                    title: separator > 0 ? name.substring(separator + 3).trim() : name,
                    duration: await readDuration(url)
                });
            }
            resolve(result);
        };
        input.oncancel = () => resolve([]);
        input.click();
    });
    return {
        download: (name, content) => {
            const url = URL.createObjectURL(new Blob([content], { type: "audio/x-mpegurl" }));
            const a = document.createElement("a");
            a.href = url;
            a.download = name;
            a.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        },
        pickPlaylist,
        pickAudioLibrary,
        loadAndPlay: async (source, volume) => {
            audio.src = source;
            audio.volume = volume;
            await audio.play();
        },
        pause: () => audio.pause(),
        seek: seconds => { audio.currentTime = seconds; },
        setVolume: volume => { audio.volume = volume; },
        state: () => ({ position: audio.currentTime || 0, duration: audio.duration || 0 })
    };
})();
