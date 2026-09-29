// Minimal Web Audio interop for the ASCII game.
// This module intentionally contains no game logic: it only synthesizes the sounds C# hands it
// (see SoundLibrary.ini) and plays them by name.

let audioContext = null;
let masterGain = null;
let noiseBuffer = null;
let sounds = new Map();
let unlockHandler = null;

export function initialize(masterVolume) {
    audioContext = new (window.AudioContext || window.webkitAudioContext)();
    masterGain = audioContext.createGain();
    masterGain.gain.value = masterVolume;
    masterGain.connect(audioContext.destination);

    // Browsers keep an AudioContext suspended until the user has interacted with the page, so it
    // is resumed on the first key press/click.
    unlockHandler = () => {
        if (audioContext && audioContext.state === 'suspended') {
            audioContext.resume();
        }
    };
    window.addEventListener('keydown', unlockHandler);
    window.addEventListener('pointerdown', unlockHandler);
}

export function setSounds(definitions) {
    sounds = new Map(definitions.map((definition) => [definition.name.toLowerCase(), definition]));
}

export function play(name) {
    if (!audioContext || audioContext.state !== 'running') {
        return;
    }

    const definition = sounds.get(name.toLowerCase());
    if (!definition || definition.durationSeconds <= 0 || definition.volume <= 0) {
        return;
    }

    const now = audioContext.currentTime;
    const end = now + definition.durationSeconds;
    const attack = Math.min(Math.max(definition.attackSeconds, 0.002), definition.durationSeconds);

    // Volume envelope: quick fade in, then a fade out over the rest of the duration.
    const envelope = audioContext.createGain();
    envelope.gain.setValueAtTime(0, now);
    envelope.gain.linearRampToValueAtTime(definition.volume, now + attack);
    envelope.gain.linearRampToValueAtTime(0, end);
    envelope.connect(masterGain);

    const startFrequency = Math.max(definition.startFrequency, 1);
    const endFrequency = Math.max(definition.endFrequency, 1);

    if (definition.wave.toLowerCase() === 'noise') {
        // Noise is filtered so its pitch slide is audible as a sweeping "whoosh".
        const source = audioContext.createBufferSource();
        source.buffer = getNoiseBuffer();
        source.loop = true;
        const filter = audioContext.createBiquadFilter();
        filter.type = 'bandpass';
        filter.frequency.setValueAtTime(startFrequency, now);
        filter.frequency.exponentialRampToValueAtTime(endFrequency, end);
        if (definition.vibratoHz > 0) {
            // Vibrato on noise wobbles the filter's pitch, giving a watery/bubbling texture.
            const wobble = audioContext.createOscillator();
            const wobbleDepth = audioContext.createGain();
            wobble.frequency.value = definition.vibratoHz;
            wobbleDepth.gain.value = Math.max(startFrequency, endFrequency) * 0.25;
            wobble.connect(wobbleDepth);
            wobbleDepth.connect(filter.frequency);
            wobble.start(now);
            wobble.stop(end);
        }
        source.connect(filter);
        filter.connect(envelope);
        source.start(now);
        source.stop(end);
        return;
    }

    const oscillator = audioContext.createOscillator();
    oscillator.type = toOscillatorType(definition.wave);
    oscillator.frequency.setValueAtTime(startFrequency, now);
    oscillator.frequency.exponentialRampToValueAtTime(endFrequency, end);

    if (definition.vibratoHz > 0) {
        const vibrato = audioContext.createOscillator();
        const vibratoDepth = audioContext.createGain();
        vibrato.frequency.value = definition.vibratoHz;
        vibratoDepth.gain.value = Math.max(startFrequency, endFrequency) * 0.05;
        vibrato.connect(vibratoDepth);
        vibratoDepth.connect(oscillator.frequency);
        vibrato.start(now);
        vibrato.stop(end);
    }

    oscillator.connect(envelope);
    oscillator.start(now);
    oscillator.stop(end);
}

function toOscillatorType(wave) {
    switch (wave.toLowerCase()) {
        case 'sine':
            return 'sine';
        case 'triangle':
            return 'triangle';
        case 'sawtooth':
            return 'sawtooth';
        default:
            return 'square';
    }
}

function getNoiseBuffer() {
    if (!noiseBuffer) {
        noiseBuffer = audioContext.createBuffer(1, audioContext.sampleRate, audioContext.sampleRate);
        const data = noiseBuffer.getChannelData(0);
        for (let i = 0; i < data.length; i++) {
            data[i] = Math.random() * 2 - 1;
        }
    }
    return noiseBuffer;
}

export function dispose() {
    if (unlockHandler) {
        window.removeEventListener('keydown', unlockHandler);
        window.removeEventListener('pointerdown', unlockHandler);
        unlockHandler = null;
    }
    if (audioContext) {
        audioContext.close();
        audioContext = null;
    }
    masterGain = null;
    noiseBuffer = null;
    sounds = new Map();
}
