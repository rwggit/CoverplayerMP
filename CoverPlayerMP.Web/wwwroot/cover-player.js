window.coverPlayer = (() => {
    const audio = new Audio();
    const pickPlaylist = () => new Promise(resolve => {
        const input = document.createElement("input");
        input.type = "file";
        input.accept = ".m3u,.m3u8,audio/x-mpegurl";
        input.onchange = async () => resolve(input.files.length ? await input.files[0].text() : null);
        input.click();
    });
    return {
        pickPlaylist,
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
